using System.Text.Json;

namespace DocLens.Api.Models;

public enum IndexingStatus
{
    Pending,
    Extracting,
    Chunking,
    Embedding,
    Indexing,
    Ready,
    Error
}

public record IndexingJobStatus(
    string DocumentId,
    IndexingStatus Status,
    int Progress,
    string? Message,
    string? Error,
    DateTimeOffset UpdatedAt
);

public record DocumentChunk(
    string Id,
    string DocumentId,
    int ChunkIndex,
    int PageNumber,
    string Content,
    float[] ContentVector,
    string? PositionsJson = null  // JSON-serialized TextPosition[] for PDF highlighting
)
{
    /// <summary>
    /// Creates a DocumentChunk with position data for PDF highlighting.
    /// </summary>
    public static DocumentChunk Create(
        string id,
        string documentId,
        TextChunk chunk,
        float[] vector)
    {
        string? positionsJson = null;
        if (chunk.Positions != null && chunk.Positions.Count > 0)
        {
            positionsJson = JsonSerializer.Serialize(chunk.Positions);
        }

        return new DocumentChunk(
            id,
            documentId,
            chunk.ChunkIndex,
            chunk.PageNumber,
            chunk.Content,
            vector,
            positionsJson
        );
    }

    /// <summary>
    /// Gets the text positions for PDF highlighting (if available).
    /// </summary>
    public IReadOnlyList<TextPosition>? GetPositions()
    {
        if (string.IsNullOrEmpty(PositionsJson))
            return null;

        return JsonSerializer.Deserialize<List<TextPosition>>(PositionsJson);
    }
}

/// <summary>
/// A search result containing a document chunk and its relevance score.
/// </summary>
/// <param name="Chunk">The matched document chunk.</param>
/// <param name="Score">Relevance score from Azure AI Search (0.0 to 1.0, higher = more relevant).</param>
public record ChunkSearchResult(
    DocumentChunk Chunk,
    double Score
);
