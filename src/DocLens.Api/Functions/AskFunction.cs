using System.Text.Json;
using DocLens.Api.Models;
using DocLens.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using OpenAI.Chat;

namespace DocLens.Api.Functions;

public class AskFunction
{
    private readonly IEmbeddingService _embedding;
    private readonly ISearchService _search;
    private readonly ChatClient _chatClient;
    private readonly ILogger<AskFunction> _logger;

    private const string SystemPrompt = """
        You are a helpful assistant that answers questions about documents.
        Use only the provided context to answer questions.
        If the answer is not in the context, say "I couldn't find information about that in the document."
        Always cite the page numbers when referencing information.
        Be concise and accurate.
        """;

    public AskFunction(
        IEmbeddingService embedding,
        ISearchService search,
        ChatClient chatClient,
        ILogger<AskFunction> logger)
    {
        _embedding = embedding;
        _search = search;
        _chatClient = chatClient;
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
            // Step 1: Generate embedding for the question
            var embeddings = await _embedding.GenerateEmbeddingsAsync([body.Question], cancellationToken);
            var queryVector = embeddings[0];

            // Step 2: Search for relevant chunks
            var chunks = await _search.SearchAsync(queryVector, documentId, topK: 5, cancellationToken);

            if (chunks.Count == 0)
            {
                await SendSseEvent(response, "chunk", new { content = "I couldn't find any relevant information in this document." }, cancellationToken);
                await SendSseEvent(response, "done", new { }, cancellationToken);
                return;
            }

            // Step 3: Build context from chunks
            var context = string.Join("\n\n", chunks.Select(c =>
                $"[Page {c.PageNumber}]: {c.Content}"));

            // Step 4: Generate response with streaming
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(SystemPrompt),
                new UserChatMessage($"Context:\n{context}\n\nQuestion: {body.Question}")
            };

            await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
            {
                foreach (var part in update.ContentUpdate)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        await SendSseEvent(response, "chunk", new { content = part.Text }, cancellationToken);
                    }
                }
            }

            // Step 5: Send source references with positions for PDF highlighting
            var sources = chunks
                .GroupBy(c => c.PageNumber)
                .Select(g =>
                {
                    var firstChunk = g.First();
                    return new SourceReference(
                        g.Key,
                        firstChunk.Content[..Math.Min(200, firstChunk.Content.Length)] + "...",
                        firstChunk.GetPositions()
                    );
                })
                .ToList();

            await SendSseEvent(response, "sources", new { sources }, cancellationToken);
            await SendSseEvent(response, "done", new { }, cancellationToken);

            _logger.LogInformation("Successfully answered question for document {DocumentId}", documentId);
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
