using Azure;
using Azure.Data.Tables;
using DocLens.Api.Models;
using DocLens.Api.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace DocLens.Api.Tests.Services;

public class ChatSessionServiceTests
{
    private readonly Mock<TableClient> _mockTableClient;
    private readonly IChatSessionService _sut;

    public ChatSessionServiceTests()
    {
        _mockTableClient = new Mock<TableClient>();
        _sut = new ChatSessionService(_mockTableClient.Object);
    }

    [Fact]
    public async Task CreateSession_ReturnsNewSessionWithId()
    {
        // Arrange
        var documentId = "doc-123";
        _mockTableClient
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<ITableEntity>(),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        // Act
        var session = await _sut.CreateSessionAsync(documentId);

        // Assert
        session.Should().NotBeNull();
        session.SessionId.Should().NotBeNullOrEmpty();
        session.DocumentId.Should().Be(documentId);
        session.Messages.Should().BeEmpty();
        session.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetSession_WithValidId_ReturnsSession()
    {
        // Arrange
        var sessionId = "session-123";
        var documentId = "doc-456";
        var createdAt = DateTime.UtcNow.AddMinutes(-10);

        var entity = new TableEntity(sessionId, "session")
        {
            { "DocumentId", documentId },
            { "MessagesJson", "[]" },
            { "CreatedAt", createdAt },
            { "UpdatedAt", createdAt }
        };

        _mockTableClient
            .Setup(x => x.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));

        // Act
        var session = await _sut.GetSessionAsync(sessionId);

        // Assert
        session.Should().NotBeNull();
        session!.SessionId.Should().Be(sessionId);
        session.DocumentId.Should().Be(documentId);
    }

    [Fact]
    public async Task GetSession_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var sessionId = "non-existent";

        _mockTableClient
            .Setup(x => x.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        // Act
        var session = await _sut.GetSessionAsync(sessionId);

        // Assert
        session.Should().BeNull();
    }

    [Fact]
    public async Task AddMessage_AppendsToHistory()
    {
        // Arrange
        var sessionId = "session-123";
        var existingEntity = new TableEntity(sessionId, "session")
        {
            { "DocumentId", "doc-456" },
            { "MessagesJson", "[]" },
            { "CreatedAt", DateTime.UtcNow },
            { "UpdatedAt", DateTime.UtcNow }
        };

        _mockTableClient
            .Setup(x => x.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existingEntity, Mock.Of<Response>()));

        TableEntity? capturedEntity = null;
        _mockTableClient
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<TableEntity>(),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()))
            .Callback<ITableEntity, TableUpdateMode, CancellationToken>((e, _, _) => capturedEntity = e as TableEntity)
            .ReturnsAsync(Mock.Of<Response>());

        var message = new ChatMessage("user", "Hello!", DateTime.UtcNow);

        // Act
        await _sut.AddMessageAsync(sessionId, message);

        // Assert
        capturedEntity.Should().NotBeNull();
        var messagesJson = capturedEntity!.GetString("MessagesJson");
        messagesJson.Should().Contain("Hello!");
    }

    [Fact]
    public async Task GetHistory_ReturnsMessagesInOrder()
    {
        // Arrange
        var sessionId = "session-123";
        var messages = new[]
        {
            new ChatMessage("user", "First", DateTime.UtcNow.AddMinutes(-2)),
            new ChatMessage("assistant", "Second", DateTime.UtcNow.AddMinutes(-1)),
            new ChatMessage("user", "Third", DateTime.UtcNow)
        };
        var messagesJson = System.Text.Json.JsonSerializer.Serialize(messages);

        var entity = new TableEntity(sessionId, "session")
        {
            { "DocumentId", "doc-456" },
            { "MessagesJson", messagesJson },
            { "CreatedAt", DateTime.UtcNow },
            { "UpdatedAt", DateTime.UtcNow }
        };

        _mockTableClient
            .Setup(x => x.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));

        // Act
        var history = await _sut.GetHistoryAsync(sessionId);

        // Assert
        history.Should().HaveCount(3);
        history[0].Content.Should().Be("First");
        history[1].Content.Should().Be("Second");
        history[2].Content.Should().Be("Third");
    }

    [Fact]
    public async Task GetHistory_LimitsToMaxMessages()
    {
        // Arrange
        var sessionId = "session-123";
        var messages = Enumerable.Range(1, 20)
            .Select(i => new ChatMessage("user", $"Message {i}", DateTime.UtcNow.AddMinutes(-20 + i)))
            .ToArray();
        var messagesJson = System.Text.Json.JsonSerializer.Serialize(messages);

        var entity = new TableEntity(sessionId, "session")
        {
            { "DocumentId", "doc-456" },
            { "MessagesJson", messagesJson },
            { "CreatedAt", DateTime.UtcNow },
            { "UpdatedAt", DateTime.UtcNow }
        };

        _mockTableClient
            .Setup(x => x.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));

        // Act
        var history = await _sut.GetHistoryAsync(sessionId, maxMessages: 5);

        // Assert
        history.Should().HaveCount(5);
        history[0].Content.Should().Be("Message 16"); // Most recent 5
        history[4].Content.Should().Be("Message 20");
    }

    [Fact]
    public async Task GetHistory_WithNonExistentSession_ReturnsEmptyList()
    {
        // Arrange
        var sessionId = "non-existent";

        _mockTableClient
            .Setup(x => x.GetEntityAsync<TableEntity>(
                sessionId,
                "session",
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        // Act
        var history = await _sut.GetHistoryAsync(sessionId);

        // Assert
        history.Should().BeEmpty();
    }
}
