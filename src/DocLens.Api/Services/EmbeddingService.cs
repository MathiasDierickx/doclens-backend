using System.ClientModel;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace DocLens.Api.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<EmbeddingService> _logger;

    // Azure OpenAI embedding limits
    private const int MaxBatchSize = 16; // Safe batch size for embeddings
    private const int MaxRetries = 5;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(60); // Azure OpenAI typically requires 60s

    public EmbeddingService(EmbeddingClient client, ILogger<EmbeddingService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        var allEmbeddings = new List<float[]>();

        // Process in batches to avoid rate limits
        var batches = texts
            .Select((text, index) => new { text, index })
            .GroupBy(x => x.index / MaxBatchSize)
            .Select(g => g.Select(x => x.text).ToList())
            .ToList();

        _logger.LogInformation(
            "Generating embeddings for {TotalTexts} texts in {BatchCount} batches",
            texts.Count, batches.Count);

        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var batch = batches[batchIndex];
            var embeddings = await GenerateBatchWithRetryAsync(batch, batchIndex, batches.Count, cancellationToken);
            allEmbeddings.AddRange(embeddings);

            // Add delay between batches to avoid rate limits
            // Azure OpenAI S0 tier has strict rate limits
            if (batchIndex < batches.Count - 1)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        return allEmbeddings;
    }

    private async Task<IReadOnlyList<float[]>> GenerateBatchWithRetryAsync(
        IReadOnlyList<string> texts,
        int batchIndex,
        int totalBatches,
        CancellationToken cancellationToken)
    {
        var retryDelay = InitialRetryDelay;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);

                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "Batch {BatchIndex}/{TotalBatches} succeeded after {Attempts} attempts",
                        batchIndex + 1, totalBatches, attempt);
                }

                return response.Value
                    .Select(e => e.ToFloats().ToArray())
                    .ToList();
            }
            catch (ClientResultException ex) when (IsRateLimitError(ex) && attempt < MaxRetries)
            {
                // Extract retry-after if available, otherwise use exponential backoff
                var waitTime = GetRetryAfterOrDefault(ex, retryDelay);

                _logger.LogWarning(
                    "Rate limit hit for batch {BatchIndex}/{TotalBatches}, attempt {Attempt}/{MaxRetries}. Waiting {WaitSeconds}s before retry",
                    batchIndex + 1, totalBatches, attempt, MaxRetries, waitTime.TotalSeconds);

                await Task.Delay(waitTime, cancellationToken);

                // Exponential backoff for next attempt
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 60));
            }
            catch (ClientResultException ex) when (IsRateLimitError(ex))
            {
                _logger.LogError(
                    "Rate limit exceeded for batch {BatchIndex}/{TotalBatches} after {MaxRetries} attempts",
                    batchIndex + 1, totalBatches, MaxRetries);
                throw;
            }
        }

        // Should not reach here, but just in case
        throw new InvalidOperationException("Unexpected end of retry loop");
    }

    private static bool IsRateLimitError(ClientResultException ex)
    {
        // HTTP 429 = Too Many Requests (rate limit)
        // Also check for specific error messages
        return ex.Status == 429 ||
               ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan GetRetryAfterOrDefault(ClientResultException ex, TimeSpan defaultDelay)
    {
        // Try to extract Retry-After header value from exception message
        // Azure OpenAI often includes this in the response
        var message = ex.Message;

        // Look for patterns like "retry after X seconds" or "Retry-After: X"
        // Azure OpenAI format: "Please retry after 60 seconds"
        var patterns = new[] { "retry after ", "retry-after: ", "try again in ", "please retry after " };

        foreach (var pattern in patterns)
        {
            var index = message.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var startIndex = index + pattern.Length;
                var endIndex = startIndex;

                while (endIndex < message.Length && (char.IsDigit(message[endIndex]) || message[endIndex] == '.'))
                {
                    endIndex++;
                }

                if (endIndex > startIndex && double.TryParse(message[startIndex..endIndex], out var seconds))
                {
                    return TimeSpan.FromSeconds(Math.Max(seconds, 1));
                }
            }
        }

        return defaultDelay;
    }
}
