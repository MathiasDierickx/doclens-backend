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

            var pageChunks = ChunkText(content, page.PageNumber, maxChunkSize, overlapSize, ref chunkIndex);
            chunks.AddRange(pageChunks);
        }

        return chunks;
    }

    private static List<TextChunk> ChunkText(
        string text,
        int pageNumber,
        int maxChunkSize,
        int overlapSize,
        ref int chunkIndex)
    {
        var chunks = new List<TextChunk>();

        if (text.Length <= maxChunkSize)
        {
            chunks.Add(new TextChunk(text, chunkIndex++, pageNumber, 0, text.Length));
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
                chunks.Add(new TextChunk(chunkContent, chunkIndex++, pageNumber, startIndex, endIndex));
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
}
