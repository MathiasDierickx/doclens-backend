namespace DocLens.Api.Models;

/// <summary>
/// Represents a single message in a chat conversation.
/// </summary>
public record ChatMessage(
    string Role,
    string Content,
    DateTime Timestamp,
    IReadOnlyList<SourceReference>? Sources = null
);

/// <summary>
/// Represents a chat session with conversation history.
/// </summary>
public record ChatSession(
    string SessionId,
    string DocumentId,
    IReadOnlyList<ChatMessage> Messages,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Context for generating a prompt response, including relevant document chunks and optional chat history.
/// </summary>
public record PromptContext(
    string Question,
    IReadOnlyList<DocumentChunk> RelevantChunks,
    IReadOnlyList<ChatMessage>? ChatHistory = null
);

/// <summary>
/// Summary of a chat session without full message content.
/// </summary>
public record ChatSessionSummary(
    string SessionId,
    string DocumentId,
    int MessageCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Response containing list of chat sessions.
/// </summary>
public record ChatSessionsResponse(
    IReadOnlyList<ChatSessionSummary> Sessions
);

/// <summary>
/// Response containing chat history for a session.
/// </summary>
public record ChatHistoryResponse(
    string SessionId,
    string DocumentId,
    IReadOnlyList<ChatMessageDto> Messages
);

/// <summary>
/// DTO for chat message with sources for API responses.
/// </summary>
public record ChatMessageDto(
    string Role,
    string Content,
    DateTime Timestamp,
    IReadOnlyList<SourceReference>? Sources = null
);
