using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using DocLens.Api.Models;

namespace DocLens.Api.Services;

public class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly DocumentAnalysisClient _client;

    public DocumentIntelligenceService(DocumentAnalysisClient client)
    {
        _client = client;
    }

    public async Task<ExtractedDocument> ExtractTextAsync(
        Stream documentStream,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            documentStream,
            cancellationToken: cancellationToken);

        var result = operation.Value;
        var pages = new List<ExtractedPage>();

        foreach (var page in result.Pages)
        {
            var pageContent = string.Join("\n",
                result.Paragraphs
                    .Where(p => p.BoundingRegions.Any(r => r.PageNumber == page.PageNumber))
                    .Select(p => p.Content));

            // Fallback to lines if paragraphs are empty
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                pageContent = string.Join("\n", page.Lines.Select(l => l.Content));
            }

            if (!string.IsNullOrWhiteSpace(pageContent))
            {
                pages.Add(new ExtractedPage(page.PageNumber, pageContent));
            }
        }

        return new ExtractedDocument(documentId, pages);
    }
}
