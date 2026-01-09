using DocLens.Api.Models;

namespace DocLens.Api.Services;

/// <summary>
/// Service for managing chat sessions and conversation history.
/// </summary>
public interface IChatSessionService
{
    /// <summary>
    /// Gets a chat session by its ID.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chat session, or null if not found.</returns>
    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new chat session for a document.
    /// </summary>
    /// <param name="documentId">The document ID this session is for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created session.</returns>
    Task<ChatSession> CreateSessionAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a message to an existing chat session.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="message">The message to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddMessageAsync(string sessionId, ChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the chat history for a session, limited to the most recent messages.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="maxMessages">Maximum number of messages to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chat messages in chronological order.</returns>
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string sessionId,
        int maxMessages = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all chat sessions for a document.
    /// </summary>
    /// <param name="documentId">The document ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of chat sessions for the document.</returns>
    Task<IReadOnlyList<ChatSession>> GetSessionsByDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
