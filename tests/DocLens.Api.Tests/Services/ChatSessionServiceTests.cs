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
    public async Task CreateSession_StoresWithDocumentIdAsPartitionKey()
    {
        // Arrange
        var documentId = "doc-123";
        TableEntity? capturedEntity = null;

        _mockTableClient
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<ITableEntity>(),
                TableUpdateMode.Replace,
                It.IsAny<CancellationToken>()))
            .Callback<ITableEntity, TableUpdateMode, CancellationToken>((e, _, _) => capturedEntity = e as TableEntity)
            .ReturnsAsync(Mock.Of<Response>());

        // Act
        await _sut.CreateSessionAsync(documentId);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.PartitionKey.Should().Be(documentId);
        capturedEntity.RowKey.Should().StartWith("session_");
    }

    [Fact]
    public async Task GetSession_WithValidId_ReturnsSession()
    {
        // Arrange
        var sessionId = "session-123";
        var documentId = "doc-456";
        var createdAt = DateTime.UtcNow.AddMinutes(-10);

        // Session metadata entity
        var sessionEntity = new TableEntity(documentId, $"session_{sessionId}")
        {
            { "SessionId", sessionId },
            { "DocumentId", documentId },
            { "CreatedAt", createdAt },
            { "UpdatedAt", createdAt }
        };

        // Setup query for session metadata
        SetupQueryAsync(_mockTableClient, new[] { sessionEntity });

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
        // Arrange - return empty list for query
        SetupQueryAsync(_mockTableClient, Array.Empty<TableEntity>());

        // Act
        var session = await _sut.GetSessionAsync("non-existent");

        // Assert
        session.Should().BeNull();
    }

    [Fact]
    public async Task AddMessage_CreatesNewMessageRow()
    {
        // Arrange
        var sessionId = "session-123";
        var documentId = "doc-456";

        // Session metadata for UpdateSessionTimestamp
        var sessionEntity = new TableEntity(documentId, $"session_{sessionId}")
        {
            { "SessionId", sessionId },
            { "DocumentId", documentId },
            { "CreatedAt", DateTime.UtcNow },
            { "UpdatedAt", DateTime.UtcNow }
        };

        SetupQueryAsync(_mockTableClient, new[] { sessionEntity });

        TableEntity? capturedMessageEntity = null;
        _mockTableClient
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<ITableEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Callback<ITableEntity, TableUpdateMode, CancellationToken>((e, _, _) =>
            {
                var entity = e as TableEntity;
                if (entity?.RowKey?.StartsWith("msg_") == true)
                    capturedMessageEntity = entity;
            })
            .ReturnsAsync(Mock.Of<Response>());

        var message = new ChatMessage("user", "Hello!", DateTime.UtcNow);

        // Act
        await _sut.AddMessageAsync(sessionId, message);

        // Assert
        capturedMessageEntity.Should().NotBeNull();
        capturedMessageEntity!.PartitionKey.Should().Be(sessionId);
        capturedMessageEntity.RowKey.Should().StartWith("msg_");
        capturedMessageEntity.GetString("Content").Should().Be("Hello!");
        capturedMessageEntity.GetString("Role").Should().Be("user");
    }

    [Fact]
    public async Task GetHistory_ReturnsMessagesInOrder()
    {
        // Arrange
        var sessionId = "session-123";
        var now = DateTime.UtcNow;

        var messageEntities = new[]
        {
            CreateMessageEntity(sessionId, "user", "First", now.AddMinutes(-2)),
            CreateMessageEntity(sessionId, "assistant", "Second", now.AddMinutes(-1)),
            CreateMessageEntity(sessionId, "user", "Third", now)
        };

        SetupQueryAsync(_mockTableClient, messageEntities);

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
        var now = DateTime.UtcNow;

        var messageEntities = Enumerable.Range(1, 20)
            .Select(i => CreateMessageEntity(sessionId, "user", $"Message {i}", now.AddMinutes(-20 + i)))
            .ToArray();

        SetupQueryAsync(_mockTableClient, messageEntities);

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
        // Arrange - empty query result
        SetupQueryAsync(_mockTableClient, Array.Empty<TableEntity>());

        // Act
        var history = await _sut.GetHistoryAsync("non-existent");

        // Assert
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessionsByDocument_ReturnsSessionsForDocument()
    {
        // Arrange
        var documentId = "doc-123";
        var now = DateTime.UtcNow;

        var sessionEntities = new[]
        {
            new TableEntity(documentId, "session_sess-1")
            {
                { "SessionId", "sess-1" },
                { "DocumentId", documentId },
                { "CreatedAt", now.AddHours(-2) },
                { "UpdatedAt", now.AddHours(-1) }
            },
            new TableEntity(documentId, "session_sess-2")
            {
                { "SessionId", "sess-2" },
                { "DocumentId", documentId },
                { "CreatedAt", now.AddHours(-1) },
                { "UpdatedAt", now }
            }
        };

        SetupQueryAsync(_mockTableClient, sessionEntities);

        // Act
        var sessions = await _sut.GetSessionsByDocumentAsync(documentId);

        // Assert
        sessions.Should().HaveCount(2);
        sessions[0].SessionId.Should().Be("sess-2"); // Most recently updated first
        sessions[1].SessionId.Should().Be("sess-1");
    }

    private static TableEntity CreateMessageEntity(string sessionId, string role, string content, DateTime timestamp)
    {
        return new TableEntity(sessionId, $"msg_{timestamp:o}_{Guid.NewGuid().ToString()[..8]}")
        {
            { "Role", role },
            { "Content", content },
            { "Timestamp", timestamp },
            { "SourcesJson", null }
        };
    }

    private static void SetupQueryAsync(Mock<TableClient> mock, TableEntity[] entities)
    {
        var asyncPageable = new MockAsyncPageable<TableEntity>(entities);
        mock
            .Setup(x => x.QueryAsync<TableEntity>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(asyncPageable);
    }

    private class MockAsyncPageable<T> : AsyncPageable<T>
    {
        private readonly T[] _items;

        public MockAsyncPageable(T[] items)
        {
            _items = items;
        }

        public override async IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            yield return Page<T>.FromValues(_items, null, Mock.Of<Response>());
            await Task.CompletedTask;
        }
    }
}
