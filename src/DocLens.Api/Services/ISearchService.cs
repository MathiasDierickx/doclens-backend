using DocLens.Api.Models;

namespace DocLens.Api.Services;

public interface ISearchService
{
    /// <summary>
    /// Indexes document chunks in Azure AI Search.
    /// </summary>
    /// <param name="chunks">The chunks to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IndexChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for relevant chunks using vector similarity.
    /// </summary>
    /// <param name="queryVector">The query embedding vector.</param>
    /// <param name="documentId">The document ID to filter by.</param>
    /// <param name="topK">Number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most relevant chunks.</returns>
    Task<IReadOnlyList<DocumentChunk>> SearchAsync(
        float[] queryVector,
        string documentId,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the search index exists with the correct schema.
    /// </summary>
    Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default);
}
