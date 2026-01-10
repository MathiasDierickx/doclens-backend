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
    /// Automatically expands context by including neighboring chunks.
    /// </summary>
    /// <param name="queryText">The original query text for keyword search.</param>
    /// <param name="queryVector">The query embedding vector for semantic search.</param>
    /// <param name="documentId">The document ID to filter by.</param>
    /// <param name="topK">Number of top matching results to find.</param>
    /// <param name="contextWindow">Number of chunks before/after each match to include (default 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most relevant chunks with their relevance scores, including neighboring context.</returns>
    Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        string queryText,
        float[] queryVector,
        string documentId,
        int topK = 5,
        int contextWindow = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the search index exists with the correct schema.
    /// </summary>
    Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default);
}
