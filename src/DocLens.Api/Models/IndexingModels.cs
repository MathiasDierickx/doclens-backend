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
    float[] ContentVector
);
