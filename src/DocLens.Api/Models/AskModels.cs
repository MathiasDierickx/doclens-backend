namespace DocLens.Api.Models;

/// <summary>
/// Request to ask a question about a document.
/// </summary>
/// <param name="Question">The question to ask.</param>
/// <param name="SessionId">Optional session ID for maintaining chat history. If not provided, a new session is created.</param>
public record AskRequest(string Question, string? SessionId = null);

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
