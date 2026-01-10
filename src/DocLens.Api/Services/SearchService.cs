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
                new SimpleField("chunkIndex", SearchFieldDataType.Int32),
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
        var results = new List<ChunkSearchResult>();

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
            results.Add(new ChunkSearchResult(chunk, score));
        }

        return results;
    }
}
