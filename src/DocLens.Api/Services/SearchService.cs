using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using DocLens.Api.Models;

namespace DocLens.Api.Services;

public class SearchService : ISearchService
{
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly string _indexName;

    public SearchService(SearchClient searchClient, SearchIndexClient indexClient, string indexName)
    {
        _searchClient = searchClient;
        _indexClient = indexClient;
        _indexName = indexName;
    }

    public async Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default)
    {
        var index = new SearchIndex(_indexName)
        {
            Fields =
            [
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("chunkIndex", SearchFieldDataType.Int32) { IsFilterable = true },
                new SimpleField("pageNumber", SearchFieldDataType.Int32) { IsFilterable = true },
                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnLucene },
                new VectorSearchField("contentVector", 1536, "default-vector-profile"),
                new SimpleField("positionsJson", SearchFieldDataType.String)  // For PDF highlighting
            ],
            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile("default-vector-profile", "default-hnsw")
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("default-hnsw")
                    {
                        Parameters = new HnswParameters
                        {
                            Metric = VectorSearchAlgorithmMetric.Cosine,
                            M = 4,
                            EfConstruction = 400,
                            EfSearch = 500
                        }
                    }
                }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
    }

    public async Task IndexChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return;

        var documents = chunks.Select(c => new SearchDocument
        {
            ["id"] = c.Id,
            ["documentId"] = c.DocumentId,
            ["chunkIndex"] = c.ChunkIndex,
            ["pageNumber"] = c.PageNumber,
            ["content"] = c.Content,
            ["contentVector"] = c.ContentVector,
            ["positionsJson"] = c.PositionsJson
        });

        await _searchClient.IndexDocumentsAsync(
            IndexDocumentsBatch.Upload(documents),
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        string queryText,
        float[] queryVector,
        string documentId,
        int topK = 5,
        int contextWindow = 1,
        CancellationToken cancellationToken = default)
    {
        // Use hybrid search: combines keyword (BM25) + vector search with RRF ranking
        var searchOptions = new SearchOptions
        {
            Filter = $"documentId eq '{documentId}'",
            Size = topK,
            QueryType = SearchQueryType.Simple,  // Enable keyword search
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryVector)
                    {
                        KNearestNeighborsCount = topK,
                        Fields = { "contentVector" }
                    }
                }
            }
        };

        // Pass queryText for keyword search (searches the 'content' field)
        // Azure AI Search combines both using Reciprocal Rank Fusion (RRF)
        var response = await _searchClient.SearchAsync<SearchDocument>(queryText, searchOptions, cancellationToken);
        var matchedChunks = new List<ChunkSearchResult>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            var chunk = new DocumentChunk(
                doc["id"].ToString()!,
                doc["documentId"].ToString()!,
                Convert.ToInt32(doc["chunkIndex"]),
                Convert.ToInt32(doc["pageNumber"]),
                doc["content"].ToString()!,
                [],  // Don't return vector in search results
                doc.TryGetValue("positionsJson", out var posJson) ? posJson?.ToString() : null
            );
            // RRF score is typically between 0 and 1, normalized
            var score = result.Score ?? 0.0;
            matchedChunks.Add(new ChunkSearchResult(chunk, score));
        }

        // If no context window requested, return as-is
        if (contextWindow <= 0 || matchedChunks.Count == 0)
        {
            return matchedChunks;
        }

        // Expand context: fetch neighboring chunks for each matched chunk
        return await ExpandContextAsync(matchedChunks, documentId, contextWindow, cancellationToken);
    }

    /// <summary>
    /// Expands search results by fetching neighboring chunks (before and after each match).
    /// This ensures we don't miss context that spans chunk boundaries.
    /// </summary>
    private async Task<IReadOnlyList<ChunkSearchResult>> ExpandContextAsync(
        List<ChunkSearchResult> matchedChunks,
        string documentId,
        int contextWindow,
        CancellationToken cancellationToken)
    {
        // Collect all chunk indices we need (matched + neighbors)
        var neededIndices = new HashSet<int>();
        var matchedIndicesWithScores = new Dictionary<int, double>();

        foreach (var match in matchedChunks)
        {
            var idx = match.Chunk.ChunkIndex;
            matchedIndicesWithScores[idx] = match.Score;

            // Add the chunk and its neighbors
            for (var i = idx - contextWindow; i <= idx + contextWindow; i++)
            {
                if (i >= 0) neededIndices.Add(i);
            }
        }

        // Fetch all needed chunks in a single query
        // Build OR filter for chunk indices (search.in only works with strings)
        var chunkFilter = string.Join(" or ", neededIndices.OrderBy(i => i).Select(i => $"chunkIndex eq {i}"));
        var filter = $"documentId eq '{documentId}' and ({chunkFilter})";

        var options = new SearchOptions
        {
            Filter = filter,
            Size = neededIndices.Count,
            QueryType = SearchQueryType.Simple
        };

        // Use empty search text with filter to fetch specific chunks
        var response = await _searchClient.SearchAsync<SearchDocument>(null, options, cancellationToken);
        var allChunks = new Dictionary<int, DocumentChunk>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            var chunkIndex = Convert.ToInt32(doc["chunkIndex"]);
            var chunk = new DocumentChunk(
                doc["id"].ToString()!,
                doc["documentId"].ToString()!,
                chunkIndex,
                Convert.ToInt32(doc["pageNumber"]),
                doc["content"].ToString()!,
                [],
                doc.TryGetValue("positionsJson", out var posJson) ? posJson?.ToString() : null
            );
            allChunks[chunkIndex] = chunk;
        }

        // Build result list: sort by chunk index to maintain document order
        // Assign scores: matched chunks keep their score, neighbors get a reduced score
        var results = new List<ChunkSearchResult>();
        var processedIndices = new HashSet<int>();

        foreach (var idx in allChunks.Keys.OrderBy(i => i))
        {
            if (processedIndices.Contains(idx)) continue;
            processedIndices.Add(idx);

            var chunk = allChunks[idx];

            // If this was a matched chunk, use its original score
            // Otherwise, it's a context chunk - give it a lower score based on distance from nearest match
            double score;
            if (matchedIndicesWithScores.TryGetValue(idx, out var matchScore))
            {
                score = matchScore;
            }
            else
            {
                // Find nearest matched chunk and calculate diminished score
                var nearestMatchScore = matchedChunks
                    .Where(m => Math.Abs(m.Chunk.ChunkIndex - idx) <= contextWindow)
                    .OrderBy(m => Math.Abs(m.Chunk.ChunkIndex - idx))
                    .Select(m => m.Score * (1.0 - 0.2 * Math.Abs(m.Chunk.ChunkIndex - idx)))
                    .FirstOrDefault();
                score = nearestMatchScore > 0 ? nearestMatchScore : 0.1;
            }

            results.Add(new ChunkSearchResult(chunk, score));
        }

        return results;
    }
}
