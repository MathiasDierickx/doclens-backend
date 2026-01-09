using DocLens.Api.Models;

namespace DocLens.Api.Services;

public interface IChunkingService
{
    /// <summary>
    /// Splits an extracted document into chunks suitable for embedding.
    /// </summary>
    /// <param name="document">The extracted document with pages.</param>
    /// <param name="maxChunkSize">Maximum characters per chunk (default ~500 tokens ≈ 2000 chars).</param>
    /// <param name="overlapSize">Overlap between chunks for context continuity (default ~50 tokens ≈ 200 chars).</param>
    /// <returns>A list of text chunks with metadata.</returns>
    IReadOnlyList<TextChunk> ChunkDocument(
        ExtractedDocument document,
        int maxChunkSize = 2000,
        int overlapSize = 200);
}
