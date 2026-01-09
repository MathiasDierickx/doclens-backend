using System.Drawing;
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
            var paragraphs = new List<ExtractedParagraph>();
            var charOffset = 0;

            // Get paragraphs for this page with bounding boxes
            var pageParagraphs = result.Paragraphs
                .Where(p => p.BoundingRegions.Any(r => r.PageNumber == page.PageNumber))
                .ToList();

            foreach (var para in pageParagraphs)
            {
                var boundingBox = ExtractBoundingBox(para.BoundingRegions.FirstOrDefault());

                paragraphs.Add(new ExtractedParagraph(
                    para.Content,
                    page.PageNumber,
                    boundingBox,
                    charOffset,
                    para.Content.Length
                ));

                charOffset += para.Content.Length + 1; // +1 for newline
            }

            var pageContent = string.Join("\n", paragraphs.Select(p => p.Content));

            // Fallback to lines if paragraphs are empty
            if (string.IsNullOrWhiteSpace(pageContent))
            {
                charOffset = 0;
                foreach (var line in page.Lines)
                {
                    var boundingBox = ExtractBoundingBoxFromPolygon(line.BoundingPolygon);

                    paragraphs.Add(new ExtractedParagraph(
                        line.Content,
                        page.PageNumber,
                        boundingBox,
                        charOffset,
                        line.Content.Length
                    ));

                    charOffset += line.Content.Length + 1;
                }
                pageContent = string.Join("\n", page.Lines.Select(l => l.Content));
            }

            if (!string.IsNullOrWhiteSpace(pageContent))
            {
                pages.Add(new ExtractedPage(
                    page.PageNumber,
                    pageContent,
                    page.Width,
                    page.Height,
                    paragraphs
                ));
            }
        }

        return new ExtractedDocument(documentId, pages);
    }

    private static BoundingBox? ExtractBoundingBox(BoundingRegion? region)
    {
        if (region == null)
            return null;

        var polygon = region.Value.BoundingPolygon;
        if (polygon == null || polygon.Count < 4)
            return null;

        return ExtractBoundingBoxFromPolygon(polygon);
    }

    private static BoundingBox? ExtractBoundingBoxFromPolygon(IReadOnlyList<PointF>? polygon)
    {
        if (polygon == null || polygon.Count < 4)
            return null;

        // Polygon points: upper-left, upper-right, lower-right, lower-left
        // Note: PDF coordinates have origin at bottom-left, but Document Intelligence
        // returns coordinates with origin at top-left
        var minX = polygon.Min(p => p.X);
        var maxX = polygon.Max(p => p.X);
        var minY = polygon.Min(p => p.Y);
        var maxY = polygon.Max(p => p.Y);

        return new BoundingBox(
            X: minX,
            Y: minY,  // Will need transformation for PDF viewers that use bottom-left origin
            Width: maxX - minX,
            Height: maxY - minY
        );
    }
}
