namespace DocLens.Api.Services;

public interface IEmbeddingService
{
    /// <summary>
    /// Generates embeddings for a list of text inputs using Azure OpenAI.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of embedding vectors (1536 dimensions for text-embedding-3-small).</returns>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
