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
        var chunks = new List<DocumentChunk>
        {
            new("chunk-1", documentId, 0, 1, "Content about the main topic", embedding)
        };

        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingsAsync(
                It.Is<IReadOnlyList<string>>(l => l.Contains(question)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { embedding });

        _mockSearchService
            .Setup(x => x.SearchAsync(
                embedding,
                documentId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

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
                It.IsAny<float[]>(),
                documentId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentChunk>());

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
                It.IsAny<float[]>(),
                documentId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentChunk>());

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
        var chunks = new List<DocumentChunk>
        {
            new("chunk-1", "doc-1", 0, 1, "Content from page 1, chunk 1", embedding),
            new("chunk-2", "doc-1", 1, 1, "Content from page 1, chunk 2", embedding),
            new("chunk-3", "doc-1", 2, 2, "Content from page 2", embedding)
        };
        var context = new PromptContext("Question?", chunks);

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
        var chunks = new List<DocumentChunk>
        {
            new("chunk-1", "doc-1", 0, 1, longContent, embedding)
        };
        var context = new PromptContext("Question?", chunks);

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
        var context = new PromptContext("Question?", new List<DocumentChunk>());

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
        var chunks = new List<DocumentChunk>
        {
            new("chunk-1", "doc-1", 0, 1, "Content", embedding, positionsJson)
        };
        var context = new PromptContext("Question?", chunks);

        // Act
        var sources = _sut.GetSourceReferences(context);

        // Assert
        sources.Should().HaveCount(1);
        sources[0].Positions.Should().NotBeNull();
        sources[0].Positions.Should().HaveCount(1);
    }
}
