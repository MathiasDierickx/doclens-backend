using DocLens.Api.Models;

namespace DocLens.Api.Services;

public interface IIndexingStatusService
{
    /// <summary>
    /// Updates the indexing status for a document.
    /// </summary>
    Task UpdateStatusAsync(
        string documentId,
        IndexingStatus status,
        int progress,
        string? message = null,
        string? error = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current indexing status for a document.
    /// </summary>
    Task<IndexingJobStatus?> GetStatusAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
