using Azure.AI.OpenAI;
using OpenAI.Embeddings;

namespace DocLens.Api.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;

    public EmbeddingService(EmbeddingClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        var response = await _client.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);

        return response.Value
            .Select(e => e.ToFloats().ToArray())
            .ToList();
    }
}
