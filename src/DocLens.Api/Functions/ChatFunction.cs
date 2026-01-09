using System.Net;
using DocLens.Api.Models;
using DocLens.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace DocLens.Api.Functions;

public class ChatFunction
{
    private readonly IChatSessionService _chatSessionService;
    private readonly ILogger<ChatFunction> _logger;

    public ChatFunction(
        IChatSessionService chatSessionService,
        ILogger<ChatFunction> logger)
    {
        _chatSessionService = chatSessionService;
        _logger = logger;
    }

    [Function("GetChatSessions")]
    [OpenApiOperation(operationId: "getChatSessions", tags: ["Chat"], Summary = "Get chat sessions", Description = "Get all chat sessions for a document")]
    [OpenApiParameter(name: "documentId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Document ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(ChatSessionsResponse), Description = "List of chat sessions")]
    public async Task<IActionResult> GetChatSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/{documentId}/chat-sessions")] HttpRequest req,
        string documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting chat sessions for document {DocumentId}", documentId);

        var sessions = await _chatSessionService.GetSessionsByDocumentAsync(documentId, cancellationToken);

        var summaries = sessions.Select(s => new ChatSessionSummary(
            s.SessionId,
            s.DocumentId,
            s.Messages.Count,
            s.CreatedAt,
            s.UpdatedAt
        )).ToList();

        return new OkObjectResult(new ChatSessionsResponse(summaries));
    }

    [Function("GetChatHistory")]
    [OpenApiOperation(operationId: "getChatHistory", tags: ["Chat"], Summary = "Get chat history", Description = "Get the chat history for a specific session")]
    [OpenApiParameter(name: "sessionId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Session ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(ChatHistoryResponse), Description = "Chat history")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Session not found")]
    public async Task<IActionResult> GetChatHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "chat-sessions/{sessionId}")] HttpRequest req,
        string sessionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting chat history for session {SessionId}", sessionId);

        var session = await _chatSessionService.GetSessionAsync(sessionId, cancellationToken);

        if (session == null)
        {
            return new NotFoundObjectResult(new ErrorResponse("Session not found"));
        }

        var messages = session.Messages.Select(m => new ChatMessageDto(
            m.Role,
            m.Content,
            m.Timestamp,
            m.Sources
        )).ToList();

        return new OkObjectResult(new ChatHistoryResponse(
            session.SessionId,
            session.DocumentId,
            messages
        ));
    }
}
