using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using DocLens.Api.Models;

namespace DocLens.Api.Services;

/// <summary>
/// Chat session service using Azure Table Storage.
///
/// Storage design:
/// - Session metadata: PartitionKey = documentId, RowKey = "session_{sessionId}"
/// - Messages: PartitionKey = sessionId, RowKey = "msg_{timestamp}_{index}"
///
/// This design allows:
/// - Efficient querying of all sessions for a document
/// - Efficient querying of all messages in a session
/// - No size limits (each message is a separate row)
/// </summary>
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
        // First, find the session metadata by querying for it
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: $"RowKey eq 'session_{sessionId}'",
            cancellationToken: cancellationToken))
        {
            var documentId = entity.GetString("DocumentId") ?? "";
            var messages = await GetMessagesAsync(sessionId, cancellationToken);

            return new ChatSession(
                sessionId,
                documentId,
                messages,
                entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
            );
        }

        return null;
    }

    public async Task<ChatSession> CreateSessionAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        // Store session metadata with documentId as partition key for efficient document queries
        var entity = new TableEntity(documentId, $"session_{sessionId}")
        {
            { "SessionId", sessionId },
            { "DocumentId", documentId },
            { "CreatedAt", now },
            { "UpdatedAt", now }
        };

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

        return new ChatSession(sessionId, documentId, [], now, now);
    }

    public async Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default)
    {
        // Create a unique, sortable row key using timestamp and a random suffix
        var timestamp = message.Timestamp.ToString("o"); // ISO 8601 format for proper sorting
        var rowKey = $"msg_{timestamp}_{Guid.NewGuid().ToString()[..8]}";

        // Compact sources to save space (keep page + score, not full text)
        var compactSources = message.Sources?
            .Select(s => new SourceReference(s.Page, Text: "", Positions: null, s.RelevanceScore))
            .ToList();

        var entity = new TableEntity(sessionId, rowKey)
        {
            { "Role", message.Role },
            { "Content", message.Content },
            { "Timestamp", message.Timestamp },
            { "SourcesJson", compactSources != null ? JsonSerializer.Serialize(compactSources, JsonOptions) : null }
        };

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);

        // Update session metadata's UpdatedAt
        await UpdateSessionTimestampAsync(sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string sessionId,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
    {
        var messages = await GetMessagesAsync(sessionId, cancellationToken);

        // Return the most recent messages, maintaining chronological order
        if (messages.Count > maxMessages)
        {
            return messages.Skip(messages.Count - maxMessages).ToList();
        }

        return messages;
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var sessions = new List<ChatSession>();

        // Query sessions by document ID (partition key) with row key prefix
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{documentId}' and RowKey ge 'session_' and RowKey lt 'session`'",
            cancellationToken: cancellationToken))
        {
            var sessionId = entity.GetString("SessionId") ?? "";

            // For listing, we don't load all messages - just metadata
            sessions.Add(new ChatSession(
                sessionId,
                documentId,
                [], // Don't load messages for listing
                entity.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                entity.GetDateTime("UpdatedAt") ?? DateTime.UtcNow
            ));
        }

        // Return sessions sorted by UpdatedAt descending (most recent first)
        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    private async Task<List<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();

        // Query all messages for this session (partition key = sessionId, row key starts with "msg_")
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{sessionId}' and RowKey ge 'msg_' and RowKey lt 'msg`'",
            cancellationToken: cancellationToken))
        {
            var sourcesJson = entity.GetString("SourcesJson");
            var sources = !string.IsNullOrEmpty(sourcesJson)
                ? JsonSerializer.Deserialize<List<SourceReference>>(sourcesJson, JsonOptions)
                : null;

            messages.Add(new ChatMessage(
                entity.GetString("Role") ?? "user",
                entity.GetString("Content") ?? "",
                entity.GetDateTime("Timestamp") ?? DateTime.UtcNow,
                sources
            ));
        }

        // Sort by timestamp (row keys are already sorted, but let's be explicit)
        return messages.OrderBy(m => m.Timestamp).ToList();
    }

    private async Task UpdateSessionTimestampAsync(string sessionId, CancellationToken cancellationToken)
    {
        // Find the session metadata and update its timestamp
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: $"RowKey eq 'session_{sessionId}'",
            maxPerPage: 1,
            cancellationToken: cancellationToken))
        {
            entity["UpdatedAt"] = DateTime.UtcNow;
            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Merge, cancellationToken);
            break;
        }
    }
}
