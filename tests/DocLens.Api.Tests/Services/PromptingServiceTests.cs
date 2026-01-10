using DocLens.Api.Models;
using DocLens.Api.Services;
using FluentAssertions;
using Moq;
using OpenAI.Chat;
using Xunit;
using ChatMessage = DocLens.Api.Models.ChatMessage;

namespace DocLens.Api.Tests.Services;

public class PromptingServiceTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly Mock<ISearchService> _mockSearchService;
    private readonly Mock<ChatClient> _mockChatClient;
    private readonly IPromptingService _sut;

    public PromptingServiceTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _mockSearchService = new Mock<ISearchService>();
        _mockChatClient = new Mock<ChatClient>();
        _sut = new PromptingService(
            _mockEmbeddingService.Object,
            _mockSearchService.Object,
            _mockChatClient.Object);
    }

    [Fact]
    public async Task BuildContext_WithQuestion_ReturnsContextWithChunks()
    {
        // Arrange
        var documentId = "doc-123";
        var question = "What is the main topic?";
        var embedding = new float[1536];
        var searchResults = new List<ChunkSearchResult>
        {
            new(new DocumentChunk("chunk-1", documentId, 0, 1, "Content about the main topic", embedding), 0.95)
        };

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingsAsync(
                It.Is<IReadOnlyList<string>>(l => l.Contains(question)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { embedding });

        _mockSearchService
            .Setup(x => x.SearchAsync(
                question,
                embedding,
                documentId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var context = await _sut.BuildContextAsync(documentId, question);

        // Assert
        context.Should().NotBeNull();
        context.Question.Should().Be(question);
        context.RelevantChunks.Should().HaveCount(1);
        context.ChatHistory.Should().BeNull();
    }

    [Fact]
    public async Task BuildContext_WithChatHistory_IncludesHistory()
    {
        // Arrange
        var documentId = "doc-123";
        var question = "What about the second point?";
        var history = new List<ChatMessage>
        {
            new("user", "What is the main topic?", DateTime.UtcNow.AddMinutes(-2)),
            new("assistant", "The main topic is...", DateTime.UtcNow.AddMinutes(-1))
        };
        var embedding = new float[1536];

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingsAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { embedding });

        _mockSearchService
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<float[]>(),
                documentId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChunkSearchResult>());

        // Act
        var context = await _sut.BuildContextAsync(documentId, question, history);

        // Assert
        context.Should().NotBeNull();
        context.Question.Should().Be(question);
        context.ChatHistory.Should().NotBeNull();
        context.ChatHistory.Should().HaveCount(2);
    }

    [Fact]
    public async Task BuildContext_UsesQuestionForEmbedding()
    {
        // Arrange
        var documentId = "doc-123";
        var question = "Specific question here";
        var embedding = new float[1536];

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingsAsync(
                It.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == question),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { embedding })
            .Verifiable();

        _mockSearchService
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<float[]>(),
                documentId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChunkSearchResult>());

        // Act
        await _sut.BuildContextAsync(documentId, question);

        // Assert
        _mockEmbeddingService.Verify();
    }

    [Fact]
    public void GetSourceReferences_GroupsByPageNumber()
    {
        // Arrange
        var embedding = new float[1536];
        var searchResults = new List<ChunkSearchResult>
        {
            new(new DocumentChunk("chunk-1", "doc-1", 0, 1, "Content from page 1, chunk 1", embedding), 0.95),
            new(new DocumentChunk("chunk-2", "doc-1", 1, 1, "Content from page 1, chunk 2", embedding), 0.85),
            new(new DocumentChunk("chunk-3", "doc-1", 2, 2, "Content from page 2", embedding), 0.75)
        };
        var context = new PromptContext("Question?", searchResults);

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().HaveCount(2); // Grouped by page
        sources.Should().Contain(s => s.Page == 1);
        sources.Should().Contain(s => s.Page == 2);
    }

    [Fact]
    public void GetSourceReferences_TruncatesLongContent()
    {
        // Arrange
        var embedding = new float[1536];
        var longContent = new string('a', 500);
        var searchResults = new List<ChunkSearchResult>
        {
            new(new DocumentChunk("chunk-1", "doc-1", 0, 1, longContent, embedding), 0.90)
        };
        var context = new PromptContext("Question?", searchResults);

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().HaveCount(1);
        sources[0].Text.Length.Should().BeLessOrEqualTo(203); // 200 + "..."
    }

    [Fact]
    public void GetSourceReferences_WithEmptyChunks_ReturnsEmptyList()
    {
        // Arrange
        var context = new PromptContext("Question?", new List<ChunkSearchResult>());

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().BeEmpty();
    }

    [Fact]
    public void GetSourceReferences_IncludesPositionsFromChunks()
    {
        // Arrange
        var embedding = new float[1536];
        var positions = new List<TextPosition>
        {
            new(1, new BoundingBox(0, 0, 100, 20), 0, 50)
        };
        var positionsJson = System.Text.Json.JsonSerializer.Serialize(positions);
        var searchResults = new List<ChunkSearchResult>
        {
            new(new DocumentChunk("chunk-1", "doc-1", 0, 1, "Content", embedding, positionsJson), 0.88)
        };
        var context = new PromptContext("Question?", searchResults);

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().HaveCount(1);
        sources[0].Positions.Should().NotBeNull();
        sources[0].Positions.Should().HaveCount(1);
    }

    [Fact]
    public void GetSourceReferences_IncludesRelevanceScore()
    {
        // Arrange
        var embedding = new float[1536];
        var searchResults = new List<ChunkSearchResult>
        {
            new(new DocumentChunk("chunk-1", "doc-1", 0, 1, "High relevance content", embedding), 0.95),
            new(new DocumentChunk("chunk-2", "doc-1", 1, 2, "Lower relevance content", embedding), 0.72)
        };
        var context = new PromptContext("Question?", searchResults);

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().HaveCount(2);
        sources[0].RelevanceScore.Should().Be(0.95);
        sources[1].RelevanceScore.Should().Be(0.72);
    }

    [Fact]
    public void GetSourceReferences_SortsByRelevanceScore()
    {
        // Arrange
        var embedding = new float[1536];
        // Insert in wrong order to verify sorting
        var searchResults = new List<ChunkSearchResult>
        {
            new(new DocumentChunk("chunk-1", "doc-1", 0, 2, "Lower relevance", embedding), 0.72),
            new(new DocumentChunk("chunk-2", "doc-1", 1, 1, "Highest relevance", embedding), 0.95),
            new(new DocumentChunk("chunk-3", "doc-1", 2, 3, "Medium relevance", embedding), 0.85)
        };
        var context = new PromptContext("Question?", searchResults);

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().HaveCount(3);
        sources[0].Page.Should().Be(1); // Highest score
        sources[1].Page.Should().Be(3); // Medium score
        sources[2].Page.Should().Be(2); // Lowest score
    }

    [Fact]
    public void GetSourceReferences_KeepsHighestScoreForSamePage()
    {
        // Arrange
        var embedding = new float[1536];
        var searchResults = new List<ChunkSearchResult>
        {
            new(new DocumentChunk("chunk-1", "doc-1", 0, 1, "Chunk 1 from page 1", embedding), 0.80),
            new(new DocumentChunk("chunk-2", "doc-1", 1, 1, "Chunk 2 from page 1 - higher score", embedding), 0.95),
            new(new DocumentChunk("chunk-3", "doc-1", 2, 2, "Chunk from page 2", embedding), 0.85)
        };
        var context = new PromptContext("Question?", searchResults);

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().HaveCount(2); // Only 2 pages
        var page1Source = sources.First(s => s.Page == 1);
        page1Source.RelevanceScore.Should().Be(0.95); // Should keep the higher score
    }
}
