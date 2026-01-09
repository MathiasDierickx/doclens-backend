using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocLens.Api.Functions;

public class DocumentsFunction(ILogger<DocumentsFunction> logger, IConfiguration configuration)
{
    [Function("GetUploadUrl")]
    public IActionResult GetUploadUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload-url")] HttpRequest req)
    {
        logger.LogInformation("Upload URL requested");

        // Get filename from query or generate one
        var filename = req.Query["filename"].FirstOrDefault();
        if (string.IsNullOrEmpty(filename))
        {
            return new BadRequestObjectResult(new { Error = "Filename is required" });
        }

        // Validate file extension
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension != ".pdf")
        {
            return new BadRequestObjectResult(new { Error = "Only PDF files are allowed" });
        }

        // Generate unique blob name
        var documentId = Guid.NewGuid().ToString();
        var blobName = $"{documentId}/{filename}";

        // Get connection string and container name from configuration
        var connectionString = configuration["StorageConnection"];
        var containerName = configuration["DocumentsContainer"] ?? "documents";

        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogError("StorageConnection not configured");
            return new StatusCodeResult(500);
        }

        // Create blob client
        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        // Generate SAS token for upload (valid for 15 minutes)
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b", // blob
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        return new OkObjectResult(new
        {
            DocumentId = documentId,
            UploadUrl = sasUri.ToString(),
            BlobName = blobName,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        });
    }

    [Function("GetDocuments")]
    public async Task<IActionResult> GetDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequest req)
    {
        logger.LogInformation("Documents list requested");

        var connectionString = configuration["StorageConnection"];
        var containerName = configuration["DocumentsContainer"] ?? "documents";

        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogError("StorageConnection not configured");
            return new StatusCodeResult(500);
        }

        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        var documents = new List<object>();

        await foreach (var blobItem in containerClient.GetBlobsAsync())
        {
            // Extract document ID from blob name (format: {documentId}/{filename})
            var parts = blobItem.Name.Split('/');
            if (parts.Length >= 2)
            {
                documents.Add(new
                {
                    DocumentId = parts[0],
                    Filename = parts[1],
                    Size = blobItem.Properties.ContentLength,
                    UploadedAt = blobItem.Properties.CreatedOn
                });
            }
        }

        return new OkObjectResult(new { Documents = documents });
    }

    [Function("DeleteDocument")]
    public async Task<IActionResult> DeleteDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "documents/{documentId}")] HttpRequest req,
        string documentId)
    {
        logger.LogInformation("Document deletion requested: {DocumentId}", documentId);

        var connectionString = configuration["StorageConnection"];
        var containerName = configuration["DocumentsContainer"] ?? "documents";

        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogError("StorageConnection not configured");
            return new StatusCodeResult(500);
        }

        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        // Delete all blobs with this document ID prefix
        var deleted = false;
        await foreach (var blobItem in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{documentId}/", default))
        {
            await containerClient.DeleteBlobAsync(blobItem.Name);
            deleted = true;
        }

        if (!deleted)
        {
            return new NotFoundObjectResult(new { Error = "Document not found" });
        }

        return new OkObjectResult(new { Message = "Document deleted", DocumentId = documentId });
    }
}
