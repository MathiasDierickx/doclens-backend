using System.Runtime.CompilerServices;
using DocLens.Api.Models;
using OpenAI.Chat;
using ModelsChatMessage = DocLens.Api.Models.ChatMessage;
using OpenAIChatMessage = OpenAI.Chat.ChatMessage;

namespace DocLens.Api.Services;

public class PromptingService : IPromptingService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ISearchService _searchService;
    private readonly ChatClient _chatClient;

    private const string SystemPrompt = """
        You are a helpful assistant that answers questions about documents.
        Use only the provided context to answer questions.
        If the answer is not in the context, say "I couldn't find information about that in the document."

        IMPORTANT: Only cite page numbers that appear in the current context (marked as [Page X]).
        Do NOT cite page numbers from previous conversation - only cite pages shown in the current context.
        If you mention information but the page is not in the current context, do not cite a page number for it.

        Be concise and accurate.
        """;

    private const int DefaultTopK = 5;
    private const int MaxContentPreviewLength = 200;

    public PromptingService(
        IEmbeddingService embeddingService,
        ISearchService searchService,
        ChatClient chatClient)
    {
        _embeddingService = embeddingService;
        _searchService = searchService;
        _chatClient = chatClient;
    }

    public async Task<PromptContext> BuildContextAsync(
        string documentId,
        string question,
        IReadOnlyList<ModelsChatMessage>? chatHistory = null,
        CancellationToken cancellationToken = default)
    {
        // Generate embedding for the question
        var embeddings = await _embeddingService.GenerateEmbeddingsAsync([question], cancellationToken);
        var queryVector = embeddings[0];

        // Search for relevant chunks using hybrid search (keyword + vector)
        var chunks = await _searchService.SearchAsync(question, queryVector, documentId, DefaultTopK, cancellationToken);

        return new PromptContext(question, chunks, chatHistory);
    }

    public async IAsyncEnumerable<string> GenerateAnswerStreamAsync(
        PromptContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.RelevantChunks.Count == 0)
        {
            yield return "I couldn't find any relevant information in this document.";
            yield break;
        }

        var messages = BuildChatMessages(context);

        await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }

    public IReadOnlyList<SourceReference> GetSourceReferences(PromptContext context)
    {
        if (context.RelevantChunks.Count == 0)
        {
            return [];
        }

        // Chunks are already sorted by relevance score from Azure AI Search.
        // We group by page but maintain the order of first appearance (highest relevance first).
        // When multiple chunks exist for the same page, we keep the highest score.
        var pageScores = new Dictionary<int, (ChunkSearchResult result, double maxScore)>();

        foreach (var searchResult in context.RelevantChunks)
        {
            var page = searchResult.Chunk.PageNumber;
            if (!pageScores.TryGetValue(page, out var existing) || searchResult.Score > existing.maxScore)
            {
                pageScores[page] = (searchResult, searchResult.Score);
            }
        }

        // Sort by score descending (best match first)
        var orderedSources = pageScores.Values
            .OrderByDescending(x => x.maxScore)
            .Select(x =>
            {
                var chunk = x.result.Chunk;
                var content = chunk.Content;
                var text = content.Length > MaxContentPreviewLength
                    ? content[..MaxContentPreviewLength] + "..."
                    : content;

                return new SourceReference(
                    chunk.PageNumber,
                    text,
                    chunk.GetPositions(),
                    x.maxScore
                );
            })
            .ToList();

        return orderedSources;
    }

    private List<OpenAIChatMessage> BuildChatMessages(PromptContext context)
    {
        var messages = new List<OpenAIChatMessage>
        {
            new SystemChatMessage(SystemPrompt)
        };

        // Add chat history if present
        if (context.ChatHistory != null)
        {
            foreach (var historyMessage in context.ChatHistory)
            {
                if (historyMessage.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(new UserChatMessage(historyMessage.Content));
                }
                else if (historyMessage.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(new AssistantChatMessage(historyMessage.Content));
                }
            }
        }

        // Build context from chunks (extract the Chunk from ChunkSearchResult)
        var documentContext = string.Join("\n\n", context.RelevantChunks.Select(r =>
            $"[Page {r.Chunk.PageNumber}]: {r.Chunk.Content}"));

        // Add the current question with context
        messages.Add(new UserChatMessage($"Context:\n{documentContext}\n\nQuestion: {context.Question}"));

        return messages;
    }
}
