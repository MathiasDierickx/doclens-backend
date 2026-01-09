namespace DocLens.Api.Models;

public record TextChunk(
    string Content,
    int ChunkIndex,
    int PageNumber,
    int StartOffset,
    int EndOffset
);

public record ExtractedPage(
    int PageNumber,
    string Content
);

public record ExtractedDocument(
    string DocumentId,
    IReadOnlyList<ExtractedPage> Pages
);
