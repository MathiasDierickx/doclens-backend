using System.Text.Json;
using DocLens.Api.Models;
using DocLens.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace DocLens.Api.Functions;

public class StatusFunction
{
    private readonly IIndexingStatusService _statusService;
    private readonly ILogger<StatusFunction> _logger;

    public StatusFunction(IIndexingStatusService statusService, ILogger<StatusFunction> logger)
    {
        _statusService = statusService;
        _logger = logger;
    }

    [Function("GetIndexingStatus")]
    [OpenApiOperation(operationId: "getIndexingStatus", tags: ["Documents"])]
    [OpenApiParameter(name: "documentId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
    [OpenApiResponseWithBody(statusCode: System.Net.HttpStatusCode.OK, contentType: "text/event-stream", bodyType: typeof(string), Description = "SSE stream of indexing status")]
    public async Task GetIndexingStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/{documentId}/status")] HttpRequest req,
        string documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SSE stream for document {DocumentId}", documentId);

        // Set SSE headers
        req.HttpContext.Response.Headers.ContentType = "text/event-stream";
        req.HttpContext.Response.Headers.CacheControl = "no-cache";
        req.HttpContext.Response.Headers.Connection = "keep-alive";

        var response = req.HttpContext.Response;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var status = await _statusService.GetStatusAsync(documentId, cancellationToken);

                if (status == null)
                {
                    // Document not found or not yet started
                    await SendSseEvent(response, "status", new
                    {
                        status = IndexingStatus.Pending.ToString().ToLower(),
                        progress = 0,
                        message = "Waiting for indexing to start..."
                    }, cancellationToken);
                }
                else
                {
                    var eventName = status.Status == IndexingStatus.Ready ? "complete" :
                                   status.Status == IndexingStatus.Error ? "error" : "status";

                    await SendSseEvent(response, eventName, new
                    {
                        status = status.Status.ToString().ToLower(),
                        progress = status.Progress,
                        message = status.Message,
                        error = status.Error
                    }, cancellationToken);

                    // Stop streaming if complete or error
                    if (status.Status is IndexingStatus.Ready or IndexingStatus.Error)
                    {
                        break;
                    }
                }

                await Task.Delay(1000, cancellationToken); // Poll every second
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE stream cancelled for document {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE stream for document {DocumentId}", documentId);
            await SendSseEvent(response, "error", new { error = "Internal server error" }, cancellationToken);
        }
    }

    private static async Task SendSseEvent(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var message = $"event: {eventName}\ndata: {json}\n\n";

        await response.WriteAsync(message, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
