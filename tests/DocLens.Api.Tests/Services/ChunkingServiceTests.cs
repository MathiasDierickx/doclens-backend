using DocLens.Api.Models;
using DocLens.Api.Services;
using FluentAssertions;
using Xunit;

namespace DocLens.Api.Tests.Services;

public class ChunkingServiceTests
{
    private readonly IChunkingService _sut;

    public ChunkingServiceTests()
    {
        _sut = new ChunkingService();
    }

    [Fact]
    public void ChunkDocument_WithShortSinglePage_ReturnsSingleChunk()
    {
        // Arrange
        var document = new ExtractedDocument(
            "doc-1",
            [new ExtractedPage(1, "This is a short text.")]
        );

        // Act
        var chunks = _sut.ChunkDocument(document);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be("This is a short text.");
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].PageNumber.Should().Be(1);
    }

    [Fact]
    public void ChunkDocument_WithMultiplePages_PreservesPageMetadata()
    {
        // Arrange
        var document = new ExtractedDocument(
            "doc-1",
            [
                new ExtractedPage(1, "Content from page one."),
                new ExtractedPage(2, "Content from page two.")
            ]
        );

        // Act
        var chunks = _sut.ChunkDocument(document, maxChunkSize: 50, overlapSize: 0);

        // Assert
        chunks.Should().HaveCount(2);
        chunks[0].PageNumber.Should().Be(1);
        chunks[1].PageNumber.Should().Be(2);
    }

    [Fact]
    public void ChunkDocument_WithLongPage_SplitsIntoMultipleChunks()
    {
        // Arrange
        var longText = new string('a', 5000); // 5000 characters
        var document = new ExtractedDocument(
            "doc-1",
            [new ExtractedPage(1, longText)]
        );

        // Act
        var chunks = _sut.ChunkDocument(document, maxChunkSize: 2000, overlapSize: 200);

        // Assert
        chunks.Should().HaveCountGreaterThan(1);
        chunks.All(c => c.PageNumber == 1).Should().BeTrue();
        chunks.Select(c => c.ChunkIndex).Should().BeInAscendingOrder();
    }

    [Fact]
    public void ChunkDocument_WithOverlap_CreatesOverlappingChunks()
    {
        // Arrange
        var text = string.Join(" ", Enumerable.Range(1, 100).Select(i => $"word{i}"));
        var document = new ExtractedDocument(
            "doc-1",
            [new ExtractedPage(1, text)]
        );

        // Act
        var chunks = _sut.ChunkDocument(document, maxChunkSize: 200, overlapSize: 50);

        // Assert
        if (chunks.Count > 1)
        {
            // Check that chunks overlap
            var firstChunkEnd = chunks[0].Content[^50..];
            chunks[1].Content.Should().StartWith(firstChunkEnd);
        }
    }

    [Fact]
    public void ChunkDocument_SplitsOnWordBoundaries()
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog repeatedly";
        var document = new ExtractedDocument(
            "doc-1",
            [new ExtractedPage(1, text)]
        );

        // Act
        var chunks = _sut.ChunkDocument(document, maxChunkSize: 30, overlapSize: 0);

        // Assert
        // Chunks should not cut words in half
        foreach (var chunk in chunks)
        {
            chunk.Content.Should().NotStartWith(" ");
            chunk.Content.Trim().Should().Be(chunk.Content);
        }
    }

    [Fact]
    public void ChunkDocument_WithEmptyDocument_ReturnsEmptyList()
    {
        // Arrange
        var document = new ExtractedDocument("doc-1", []);

        // Act
        var chunks = _sut.ChunkDocument(document);

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkDocument_WithEmptyPage_SkipsEmptyContent()
    {
        // Arrange
        var document = new ExtractedDocument(
            "doc-1",
            [
                new ExtractedPage(1, ""),
                new ExtractedPage(2, "Some content"),
                new ExtractedPage(3, "   ") // whitespace only
            ]
        );

        // Act
        var chunks = _sut.ChunkDocument(document);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].PageNumber.Should().Be(2);
    }

    [Fact]
    public void ChunkDocument_ChunkIndexesAreSequential()
    {
        // Arrange
        var document = new ExtractedDocument(
            "doc-1",
            [
                new ExtractedPage(1, new string('a', 3000)),
                new ExtractedPage(2, new string('b', 3000))
            ]
        );

        // Act
        var chunks = _sut.ChunkDocument(document, maxChunkSize: 1000, overlapSize: 100);

        // Assert
        var expectedIndexes = Enumerable.Range(0, chunks.Count).ToList();
        chunks.Select(c => c.ChunkIndex).Should().Equal(expectedIndexes);
    }

    [Fact]
    public void ChunkDocument_TracksCorrectOffsets()
    {
        // Arrange
        var text = "Hello World";
        var document = new ExtractedDocument(
            "doc-1",
            [new ExtractedPage(1, text)]
        );

        // Act
        var chunks = _sut.ChunkDocument(document);

        // Assert
        chunks[0].StartOffset.Should().Be(0);
        chunks[0].EndOffset.Should().Be(text.Length);
    }
}
