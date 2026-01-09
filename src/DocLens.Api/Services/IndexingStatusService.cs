using Azure.Data.Tables;
using DocLens.Api.Models;

namespace DocLens.Api.Services;

public class IndexingStatusService : IIndexingStatusService
{
    private readonly TableClient _tableClient;

    public IndexingStatusService(TableClient tableClient)
    {
        _tableClient = tableClient;
    }

    public async Task UpdateStatusAsync(
        string documentId,
        IndexingStatus status,
        int progress,
        string? message = null,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        var entity = new TableEntity(documentId, "status")
        {
            ["Status"] = status.ToString(),
            ["Progress"] = progress,
            ["Message"] = message,
            ["Error"] = error,
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        };

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<IndexingJobStatus?> GetStatusAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                documentId, "status", cancellationToken: cancellationToken);

            var entity = response.Value;
            return new IndexingJobStatus(
                documentId,
                Enum.Parse<IndexingStatus>(entity["Status"].ToString()!),
                Convert.ToInt32(entity["Progress"]),
                entity["Message"]?.ToString(),
                entity["Error"]?.ToString(),
                entity.GetDateTimeOffset("UpdatedAt") ?? DateTimeOffset.UtcNow
            );
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
