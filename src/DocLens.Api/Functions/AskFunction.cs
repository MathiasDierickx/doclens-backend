using System.Text.Json;
using DocLens.Api.Models;
using DocLens.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace DocLens.Api.Functions;

public class AskFunction
{
    private readonly IPromptingService _promptingService;
    private readonly IChatSessionService _chatSessionService;
    private readonly ILogger<AskFunction> _logger;

    public AskFunction(
        IPromptingService promptingService,
        IChatSessionService chatSessionService,
        ILogger<AskFunction> logger)
    {
        _promptingService = promptingService;
        _chatSessionService = chatSessionService;
        _logger = logger;
    }

    [Function("AskQuestion")]
    [OpenApiOperation(operationId: "askQuestion", tags: ["Documents"])]
    [OpenApiParameter(name: "documentId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(AskRequest), Required = true)]
    [OpenApiResponseWithBody(statusCode: System.Net.HttpStatusCode.OK, contentType: "text/event-stream", bodyType: typeof(string), Description = "SSE stream of answer chunks")]
    public async Task AskQuestion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/{documentId}/ask")] HttpRequest req,
        string documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing question for document {DocumentId}", documentId);

        // Parse request
        var body = await JsonSerializer.DeserializeAsync<AskRequest>(req.Body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken);

        if (body == null || string.IsNullOrWhiteSpace(body.Question))
        {
            req.HttpContext.Response.StatusCode = 400;
            await req.HttpContext.Response.WriteAsJsonAsync(new { error = "Question is required" }, cancellationToken);
            return;
        }

        // Set SSE headers
        req.HttpContext.Response.Headers.ContentType = "text/event-stream";
        req.HttpContext.Response.Headers.CacheControl = "no-cache";
        req.HttpContext.Response.Headers.Connection = "keep-alive";

        var response = req.HttpContext.Response;

        try
        {
            // Get or create chat session
            var sessionId = body.SessionId;
            IReadOnlyList<ChatMessage>? chatHistory = null;

            if (!string.IsNullOrEmpty(sessionId))
            {
                // Existing session - get history
                chatHistory = await _chatSessionService.GetHistoryAsync(sessionId, cancellationToken: cancellationToken);
            }
            else
            {
                // Create new session
                var session = await _chatSessionService.CreateSessionAsync(documentId, cancellationToken);
                sessionId = session.SessionId;
            }

            // Build context with optional chat history
            var context = await _promptingService.BuildContextAsync(
                documentId,
                body.Question,
                chatHistory,
                cancellationToken);

            // Store user message in session
            await _chatSessionService.AddMessageAsync(
                sessionId,
                new ChatMessage("user", body.Question, DateTime.UtcNow),
                cancellationToken);

            // Generate and stream the answer
            var fullAnswer = new System.Text.StringBuilder();
            await foreach (var token in _promptingService.GenerateAnswerStreamAsync(context, cancellationToken))
            {
                fullAnswer.Append(token);
                await SendSseEvent(response, "chunk", new { content = token }, cancellationToken);
            }

            // Store assistant response in session
            await _chatSessionService.AddMessageAsync(
                sessionId,
                new ChatMessage("assistant", fullAnswer.ToString(), DateTime.UtcNow),
                cancellationToken);

            // Send source references
            var sources = _promptingService.GetSourceReferences(context);
            await SendSseEvent(response, "sources", new { sources }, cancellationToken);

            // Send completion with session ID for follow-up questions
            await SendSseEvent(response, "done", new { sessionId }, cancellationToken);

            _logger.LogInformation("Successfully answered question for document {DocumentId}, session {SessionId}", documentId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error answering question for document {DocumentId}", documentId);
            await SendSseEvent(response, "error", new { error = "Failed to generate answer" }, cancellationToken);
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
