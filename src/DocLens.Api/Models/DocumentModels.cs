namespace DocLens.Api.Models;

public record UploadUrlResponse(
    string DocumentId,
    string UploadUrl,
    string BlobName,
    DateTimeOffset ExpiresAt
);

public record DocumentInfo(
    string DocumentId,
    string Filename,
    long? Size,
    DateTimeOffset? UploadedAt
);

public record DocumentListResponse(
    List<DocumentInfo> Documents
);

public record DeleteDocumentResponse(
    string Message,
    string DocumentId
);

public record ErrorResponse(
    string Error
);

public record DownloadUrlResponse(
    string DocumentId,
    string DownloadUrl,
    string Filename,
    DateTimeOffset ExpiresAt
);

public record HealthResponse(
    string Status,
    DateTime Timestamp,
    string Version
);

public record HelloResponse(
    string Message,
    DateTime Timestamp
);
