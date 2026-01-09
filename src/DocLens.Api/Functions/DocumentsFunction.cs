using System.Net;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using DocLens.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace DocLens.Api.Functions;

public class DocumentsFunction(ILogger<DocumentsFunction> logger, IConfiguration configuration)
{
    [Function("GetUploadUrl")]
    [OpenApiOperation(operationId: "getUploadUrl", tags: ["Documents"], Summary = "Get upload URL", Description = "Generate a SAS URL for direct PDF upload to Azure Blob Storage")]
    [OpenApiParameter(name: "filename", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Filename of the PDF to upload (must end with .pdf)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(UploadUrlResponse), Description = "Upload URL and document metadata")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Invalid filename or file type")]
    public IActionResult GetUploadUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload-url")] HttpRequest req)
    {
        logger.LogInformation("Upload URL requested");

        var filename = req.Query["filename"].FirstOrDefault();
        if (string.IsNullOrEmpty(filename))
        {
            return new BadRequestObjectResult(new ErrorResponse("Filename is required"));
        }

        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension != ".pdf")
        {
            return new BadRequestObjectResult(new ErrorResponse("Only PDF files are allowed"));
        }

        var documentId = Guid.NewGuid().ToString();
        var blobName = $"{documentId}/{filename}";

        var connectionString = configuration["StorageConnection"];
        var containerName = configuration["DocumentsContainer"] ?? "documents";

        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogError("StorageConnection not configured");
            return new StatusCodeResult(500);
        }

        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        return new OkObjectResult(new UploadUrlResponse(
            DocumentId: documentId,
            UploadUrl: sasUri.ToString(),
            BlobName: blobName,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15)
        ));
    }

    [Function("GetDocuments")]
    [OpenApiOperation(operationId: "listDocuments", tags: ["Documents"], Summary = "List documents", Description = "List all uploaded documents")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(DocumentListResponse), Description = "List of documents")]
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

        var documents = new List<DocumentInfo>();

        await foreach (var blobItem in containerClient.GetBlobsAsync())
        {
            var parts = blobItem.Name.Split('/');
            if (parts.Length >= 2)
            {
                documents.Add(new DocumentInfo(
                    DocumentId: parts[0],
                    Filename: parts[1],
                    Size: blobItem.Properties.ContentLength,
                    UploadedAt: blobItem.Properties.CreatedOn
                ));
            }
        }

        return new OkObjectResult(new DocumentListResponse(documents));
    }

    [Function("DeleteDocument")]
    [OpenApiOperation(operationId: "deleteDocument", tags: ["Documents"], Summary = "Delete document", Description = "Delete a document by ID")]
    [OpenApiParameter(name: "documentId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Document ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(DeleteDocumentResponse), Description = "Deletion confirmation")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(ErrorResponse), Description = "Document not found")]
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

        var deleted = false;
        await foreach (var blobItem in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{documentId}/", default))
        {
            await containerClient.DeleteBlobAsync(blobItem.Name);
            deleted = true;
        }

        if (!deleted)
        {
            return new NotFoundObjectResult(new ErrorResponse("Document not found"));
        }

        return new OkObjectResult(new DeleteDocumentResponse(
            Message: "Document deleted",
            DocumentId: documentId
        ));
    }
}
