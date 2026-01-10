using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using DocLens.Api.Models;

namespace DocLens.Api.Services;

public class ChatSessionService : IChatSessionService
{
    private readonly TableClient _tableClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ChatSessionService(TableClient tableClient)
    {
        _tableClient = tableClient;
    }

    public async Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                cancellationToken: cancellationToken);

            return EntityToSession(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<ChatSession> CreateSessionAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var entity = new TableEntity(sessionId, "session")
        {
            { "DocumentId", documentId },
            { "MessagesJson", "[]" },
            { "CreatedAt", now },
            { "UpdatedAt", now }
        };

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

        return new ChatSession(sessionId, documentId, [], now, now);
    }

    public async Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default)
    {
        var response = await _tableClient.GetEntityAsync<TableEntity>(
            sessionId,
            "session",
            cancellationToken: cancellationToken);

        var entity = response.Value;
        var messagesJson = entity.GetString("MessagesJson") ?? "[]";
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson, JsonOptions) ?? [];

        // Store message without full source content to avoid exceeding Azure Table's 64KB limit
        // Keep only page numbers and scores for reference, not full text/positions
        var compactSources = message.Sources?
            .Select(s => new SourceReference(s.Page, Text: "", Positions: null, s.RelevanceScore))
            .ToList();
        var compactMessage = message with { Sources = compactSources };

        messages.Add(compactMessage);

        entity["MessagesJson"] = JsonSerializer.Serialize(messages, JsonOptions);
        entity["UpdatedAt"] = DateTime.UtcNow;

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string sessionId,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                cancellationToken: cancellationToken);

            var messagesJson = response.Value.GetString("MessagesJson") ?? "[]";
            var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson, JsonOptions) ?? [];

            // Return the most recent messages, maintaining chronological order
            if (messages.Count > maxMessages)
            {
                return messages.Skip(messages.Count - maxMessages).ToList();
            }

            return messages;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var sessions = new List<ChatSession>();

        // Query all sessions and filter by document ID
        // Note: In a production system with many sessions, you might want to use a secondary index
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: $"RowKey eq 'session'",
            cancellationToken: cancellationToken))
        {
            if (entity.GetString("DocumentId") == documentId)
            {
                sessions.Add(EntityToSession(entity));
            }
        }

        // Return sessions sorted by UpdatedAt descending (most recent first)
        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    private static ChatSession EntityToSession(TableEntity entity)
    {
        var messagesJson = entity.GetString("MessagesJson") ?? "[]";
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson, JsonOptions) ?? [];

        return new ChatSession(
            entity.PartitionKey,
            entity.GetString("DocumentId") ?? "",
            messages,
            entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
            entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
        );
    }
}
