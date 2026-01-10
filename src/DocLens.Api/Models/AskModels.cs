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
/// <param name="Page">The page number in the document.</param>
/// <param name="Text">Preview text from this source.</param>
/// <param name="Positions">Position data for PDF highlighting.</param>
/// <param name="RelevanceScore">Relevance score from 0.0 to 1.0 (1.0 = perfect match).</param>
public record SourceReference(
    int Page,
    string Text,
    IReadOnlyList<TextPosition>? Positions = null,
    double RelevanceScore = 0.0
);
