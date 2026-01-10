using Azure.Storage.Blobs;
using DocLens.Api.Models;
using DocLens.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DocLens.Api.Functions;

public class IndexingFunction
{
    private readonly IDocumentIntelligenceService _documentIntelligence;
    private readonly IChunkingService _chunking;
    private readonly IEmbeddingService _embedding;
    private readonly ISearchService _search;
    private readonly IIndexingStatusService _status;
    private readonly ILogger<IndexingFunction> _logger;

    public IndexingFunction(
        IDocumentIntelligenceService documentIntelligence,
        IChunkingService chunking,
        IEmbeddingService embedding,
        ISearchService search,
        IIndexingStatusService status,
        ILogger<IndexingFunction> logger)
    {
        _documentIntelligence = documentIntelligence;
        _chunking = chunking;
        _embedding = embedding;
        _search = search;
        _status = status;
        _logger = logger;
    }

    [Function("IndexDocument")]
    public async Task IndexDocument(
        [BlobTrigger("documents/{documentId}/{filename}", Connection = "StorageConnection")] BlobClient blobClient,
        string documentId,
        string filename,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting indexing for document {DocumentId}, file {Filename}", documentId, filename);

        try
        {
            // Ensure search index exists
            await _search.EnsureIndexExistsAsync(cancellationToken);

            // Step 1: Extract text
            await _status.UpdateStatusAsync(documentId, IndexingStatus.Extracting, 10, "Extracting text from PDF...", cancellationToken: cancellationToken);

            using var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
            var extractedDocument = await _documentIntelligence.ExtractTextAsync(stream, documentId, cancellationToken);

            _logger.LogInformation("Extracted {PageCount} pages from document {DocumentId}", extractedDocument.Pages.Count, documentId);

            // Step 2: Chunk the document
            await _status.UpdateStatusAsync(documentId, IndexingStatus.Chunking, 30, "Splitting into chunks...", cancellationToken: cancellationToken);

            var chunks = _chunking.ChunkDocument(extractedDocument);

            _logger.LogInformation("Created {ChunkCount} chunks from document {DocumentId}", chunks.Count, documentId);

            if (chunks.Count == 0)
            {
                await _status.UpdateStatusAsync(documentId, IndexingStatus.Error, 0, error: "No text content found in document", cancellationToken: cancellationToken);
                return;
            }

            // Step 3: Generate embeddings
            await _status.UpdateStatusAsync(documentId, IndexingStatus.Embedding, 50, "Generating embeddings...", cancellationToken: cancellationToken);

            var texts = chunks.Select(c => c.Content).ToList();
            var embeddings = await _embedding.GenerateEmbeddingsAsync(texts, cancellationToken);

            // Step 4: Create document chunks with embeddings and position data
            var documentChunks = chunks.Zip(embeddings, (chunk, vector) =>
                DocumentChunk.Create($"{documentId}_{chunk.ChunkIndex}", documentId, chunk, vector)
            ).ToList();

            // Log position data for debugging
            var chunksWithPositions = documentChunks.Count(c => !string.IsNullOrEmpty(c.PositionsJson));
            _logger.LogInformation(
                "Document {DocumentId}: {Total} chunks, {WithPositions} with position data",
                documentId, documentChunks.Count, chunksWithPositions);

            // Step 5: Index in AI Search
            await _status.UpdateStatusAsync(documentId, IndexingStatus.Indexing, 80, "Indexing in search...", cancellationToken: cancellationToken);

            await _search.IndexChunksAsync(documentChunks, cancellationToken);

            // Done!
            await _status.UpdateStatusAsync(documentId, IndexingStatus.Ready, 100, "Indexing complete", cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully indexed document {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing document {DocumentId}", documentId);
            await _status.UpdateStatusAsync(documentId, IndexingStatus.Error, 0, error: ex.Message, cancellationToken: cancellationToken);
            throw;
        }
    }
}
