namespace DocLens.Api.Models;

/// <summary>
/// A bounding box representing position in the PDF.
/// Coordinates are in inches (for PDF) from bottom-left origin.
/// </summary>
public record BoundingBox(
    float X,      // Left position (inches from left edge)
    float Y,      // Bottom position (inches from bottom edge)
    float Width,  // Width in inches
    float Height  // Height in inches
);

/// <summary>
/// Position information for a text span in the document.
/// Used for highlighting text in PDF viewer.
/// </summary>
public record TextPosition(
    int PageNumber,
    BoundingBox? BoundingBox,
    int CharOffset,  // Character offset within the page
    int CharLength,  // Character length of this span
    float? PageWidth = null,   // Page width in inches (for coordinate conversion)
    float? PageHeight = null   // Page height in inches (for coordinate conversion)
);

public record TextChunk(
    string Content,
    int ChunkIndex,
    int PageNumber,
    int StartOffset,
    int EndOffset,
    IReadOnlyList<TextPosition>? Positions = null  // For PDF highlighting
);

public record ExtractedParagraph(
    string Content,
    int PageNumber,
    BoundingBox? BoundingBox,
    int CharOffset,
    int CharLength
);

public record ExtractedPage(
    int PageNumber,
    string Content,
    float? Width = null,   // Page width in inches
    float? Height = null,  // Page height in inches
    IReadOnlyList<ExtractedParagraph>? Paragraphs = null
);

public record ExtractedDocument(
    string DocumentId,
    IReadOnlyList<ExtractedPage> Pages
);
