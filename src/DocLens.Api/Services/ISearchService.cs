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
    /// Searches for relevant chunks using hybrid search (vector + keyword).
    /// Combines semantic similarity with exact keyword matching for best results.
    /// </summary>
    /// <param name="queryText">The original query text for keyword search.</param>
    /// <param name="queryVector">The query embedding vector for semantic search.</param>
    /// <param name="documentId">The document ID to filter by.</param>
    /// <param name="topK">Number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most relevant chunks with their relevance scores, sorted by score descending.</returns>
    Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        string queryText,
        float[] queryVector,
        string documentId,
        int topK = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the search index exists with the correct schema.
    /// </summary>
    Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default);
}
