namespace DocLens.Api.Models;

public record AskRequest(string Question);

public record AskResponse(
    string Answer,
    IReadOnlyList<SourceReference> Sources
);

public record SourceReference(
    int Page,
    string Text
);
