using DocLens.Api.Models;

namespace DocLens.Api.Services;

/// <summary>
/// Service for generating AI-powered responses based on document context and chat history.
/// </summary>
public interface IPromptingService
{
    /// <summary>
    /// Builds the prompt context by searching for relevant document chunks.
    /// </summary>
    /// <param name="documentId">The document ID to search in.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="chatHistory">Optional chat history for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The prompt context with relevant chunks.</returns>
    Task<PromptContext> BuildContextAsync(
        string documentId,
        string question,
        IReadOnlyList<ChatMessage>? chatHistory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a streaming answer based on the provided context.
    /// </summary>
    /// <param name="context">The prompt context with relevant chunks and optional history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of answer tokens.</returns>
    IAsyncEnumerable<string> GenerateAnswerStreamAsync(
        PromptContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the source references from the context chunks.
    /// </summary>
    /// <param name="context">The prompt context.</param>
    /// <returns>Source references with page numbers and positions for PDF highlighting.</returns>
    IReadOnlyList<SourceReference> GetSourceReferences(PromptContext context);
}
