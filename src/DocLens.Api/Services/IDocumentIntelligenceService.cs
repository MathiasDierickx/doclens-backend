using DocLens.Api.Models;

namespace DocLens.Api.Services;

public interface IDocumentIntelligenceService
{
    /// <summary>
    /// Extracts text from a PDF document using Azure Document Intelligence.
    /// </summary>
    /// <param name="documentStream">The PDF document stream.</param>
    /// <param name="documentId">The document ID for tracking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted document with pages.</returns>
    Task<ExtractedDocument> ExtractTextAsync(
        Stream documentStream,
        string documentId,
        CancellationToken cancellationToken = default);
}
