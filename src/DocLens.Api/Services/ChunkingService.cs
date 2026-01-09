using DocLens.Api.Models;

namespace DocLens.Api.Services;

public class ChunkingService : IChunkingService
{
    public IReadOnlyList<TextChunk> ChunkDocument(
        ExtractedDocument document,
        int maxChunkSize = 2000,
        int overlapSize = 200)
    {
        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        foreach (var page in document.Pages)
        {
            var content = page.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var pageChunks = ChunkText(content, page, maxChunkSize, overlapSize, ref chunkIndex);
            chunks.AddRange(pageChunks);
        }

        return chunks;
    }

    private static List<TextChunk> ChunkText(
        string text,
        ExtractedPage page,
        int maxChunkSize,
        int overlapSize,
        ref int chunkIndex)
    {
        var chunks = new List<TextChunk>();

        if (text.Length <= maxChunkSize)
        {
            var positions = FindPositionsForRange(page, 0, text.Length);
            chunks.Add(new TextChunk(text, chunkIndex++, page.PageNumber, 0, text.Length, positions));
            return chunks;
        }

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var endIndex = Math.Min(startIndex + maxChunkSize, text.Length);

            // Try to break at a word boundary if not at the end
            if (endIndex < text.Length)
            {
                var lastSpace = text.LastIndexOf(' ', endIndex - 1, Math.Min(endIndex - startIndex, maxChunkSize / 4));
                if (lastSpace > startIndex)
                {
                    endIndex = lastSpace;
                }
            }

            var chunkContent = text[startIndex..endIndex].Trim();

            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                var positions = FindPositionsForRange(page, startIndex, endIndex);
                chunks.Add(new TextChunk(chunkContent, chunkIndex++, page.PageNumber, startIndex, endIndex, positions));
            }

            // Move start index, accounting for overlap
            var step = endIndex - startIndex - overlapSize;
            if (step <= 0) step = endIndex - startIndex; // Prevent infinite loop
            startIndex += step;

            // Skip leading whitespace
            while (startIndex < text.Length && char.IsWhiteSpace(text[startIndex]))
            {
                startIndex++;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Find paragraph positions that overlap with the given character range.
    /// This allows for PDF highlighting of chunks.
    /// </summary>
    private static List<TextPosition>? FindPositionsForRange(ExtractedPage page, int startOffset, int endOffset)
    {
        if (page.Paragraphs == null || page.Paragraphs.Count == 0)
            return null;

        var positions = new List<TextPosition>();

        foreach (var para in page.Paragraphs)
        {
            var paraStart = para.CharOffset;
            var paraEnd = para.CharOffset + para.CharLength;

            // Check if paragraph overlaps with chunk range
            if (paraEnd > startOffset && paraStart < endOffset)
            {
                positions.Add(new TextPosition(
                    page.PageNumber,
                    para.BoundingBox,
                    para.CharOffset,
                    para.CharLength
                ));
            }
        }

        return positions.Count > 0 ? positions : null;
    }
}
