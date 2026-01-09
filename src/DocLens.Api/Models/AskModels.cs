namespace DocLens.Api.Models;

public record AskRequest(string Question);

public record AskResponse(
    string Answer,
    IReadOnlyList<SourceReference> Sources
);

/// <summary>
/// Reference to a source location in the PDF for highlighting.
/// </summary>
public record SourceReference(
    int Page,
    string Text,
    IReadOnlyList<TextPosition>? Positions = null  // For PDF highlighting
);
