# DocLens Backend

Document Q&A API powered by Azure Functions and Azure AI Services. Upload PDFs, automatically index them with vector embeddings, and ask questions with AI-powered answers that cite specific pages.

## Architecture

- **Runtime**: .NET 9 Azure Functions (Isolated Worker)
- **Hosting**: Azure Functions Consumption Plan (serverless)
- **Infrastructure**: Bicep + Azure Developer CLI (azd)

### Azure Services Used

| Service | Purpose |
|---------|---------|
| Azure Functions | Serverless API hosting |
| Azure Blob Storage | PDF document storage |
| Azure Table Storage | Indexing status & chat sessions |
| Azure Document Intelligence | PDF text extraction with layout |
| Azure OpenAI | Embeddings (text-embedding-3-small) & Chat (GPT-4o) |
| Azure AI Search | Vector + keyword hybrid search |
| Application Insights | Monitoring & logging |

## API Endpoints

### Documents

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/documents/upload-url?filename=doc.pdf` | POST | Get SAS URL for direct PDF upload |
| `/api/documents` | GET | List all uploaded documents |
| `/api/documents/{documentId}` | GET | Get document details |
| `/api/documents/{documentId}` | DELETE | Delete a document |
| `/api/documents/{documentId}/download-url` | GET | Get SAS URL for PDF download |
| `/api/documents/{documentId}/status` | GET | **SSE** - Stream indexing progress |
| `/api/documents/{documentId}/ask` | POST | **SSE** - Ask question, stream answer |

### Chat Sessions

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/documents/{documentId}/chat-sessions` | GET | List chat sessions for a document |
| `/api/chat-sessions/{sessionId}` | GET | Get chat history for a session |

### System

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check |
| `/api/hello?name=Name` | GET | Hello world test |

### SSE Streaming Endpoints

**Indexing Status** (`GET /api/documents/{documentId}/status`)
```
event: status
data: {"status":"extracting","progress":10,"message":"Extracting text from PDF..."}

event: status
data: {"status":"embedding","progress":50,"message":"Generating embeddings..."}

event: complete
data: {"status":"ready","progress":100,"message":"Indexing complete"}
```

**Ask Question** (`POST /api/documents/{documentId}/ask`)
```json
// Request body
{ "question": "What is the main topic?", "sessionId": "optional-session-id" }
```
```
event: chunk
data: {"content":"The"}

event: chunk
data: {"content":" main topic"}

event: sources
data: {"sources":[{"page":1,"text":"...","positions":[...]}]}

event: done
data: {"sessionId":"abc-123"}
```

## Project Structure

```
doclens-backend/
├── azure.yaml                    # azd configuration
├── infra/                        # Bicep infrastructure
│   ├── main.bicep
│   ├── main.parameters.json
│   └── modules/
│       ├── function-app.bicep
│       └── ai-services.bicep
├── src/DocLens.Api/
│   ├── Functions/
│   │   ├── HealthFunction.cs     # Health check
│   │   ├── HelloFunction.cs      # Hello world
│   │   ├── DocumentsFunction.cs  # CRUD operations
│   │   ├── IndexingFunction.cs   # Blob-triggered indexing pipeline
│   │   ├── StatusFunction.cs     # SSE indexing progress
│   │   ├── AskFunction.cs        # SSE Q&A streaming
│   │   └── ChatFunction.cs       # Chat session management
│   ├── Services/
│   │   ├── IDocumentIntelligenceService.cs  # PDF text extraction
│   │   ├── IChunkingService.cs              # Text chunking
│   │   ├── IEmbeddingService.cs             # Vector embeddings
│   │   ├── ISearchService.cs                # Azure AI Search
│   │   ├── IIndexingStatusService.cs        # Progress tracking
│   │   ├── IPromptingService.cs             # RAG prompt building
│   │   └── IChatSessionService.cs           # Chat history
│   ├── Models/
│   │   ├── DocumentModels.cs
│   │   ├── ChunkModels.cs
│   │   ├── IndexingModels.cs
│   │   ├── AskModels.cs
│   │   └── ChatModels.cs
│   └── Program.cs                # DI configuration
└── tests/DocLens.Api.Tests/
    └── Services/
        ├── ChunkingServiceTests.cs
        ├── PromptingServiceTests.cs
        └── ChatSessionServiceTests.cs
```

## Indexing Pipeline

When a PDF is uploaded to blob storage, the `IndexingFunction` is automatically triggered:

```
1. Extract (10%)  → Azure Document Intelligence extracts text + layout
2. Chunk (30%)    → Split into overlapping chunks with position data
3. Embed (50%)    → Generate embeddings via Azure OpenAI
4. Index (80%)    → Store in Azure AI Search with vectors
5. Ready (100%)   → Document ready for Q&A
```

### Rate Limit Handling

The embedding service includes built-in resilience:
- **Batching**: Processes max 16 texts per batch
- **Retry**: Up to 5 attempts with exponential backoff
- **Backoff**: Starts at 60s (Azure OpenAI requirement), doubles each retry

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)

## Local Development

### 1. Clone the repository

```bash
git clone <repository-url>
cd doclens-backend
```

### 2. Configure local settings

Create `src/DocLens.Api/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "StorageConnection": "<your-storage-connection-string>",
    "DocumentsContainer": "documents",
    "DocumentIntelligenceEndpoint": "<endpoint>",
    "DocumentIntelligenceKey": "<key>",
    "AzureOpenAIEndpoint": "<endpoint>",
    "AzureOpenAIKey": "<key>",
    "AzureOpenAIEmbeddingDeployment": "text-embedding-3-small",
    "AzureOpenAIChatDeployment": "gpt-4o",
    "AzureSearchEndpoint": "<endpoint>",
    "AzureSearchKey": "<key>",
    "AzureSearchIndexName": "documents"
  },
  "Host": {
    "CORS": "http://localhost:3000",
    "CORSCredentials": false
  }
}
```

### 3. Run locally

```bash
cd src/DocLens.Api
func start
```

The API will be available at `http://localhost:7071`.

### 4. Run tests

```bash
dotnet test
```

## Deployment to Azure

### 1. Login to Azure

```bash
az login
azd auth login
```

### 2. Initialize and Deploy

```bash
# Initialize environment (first time only)
azd init

# Set your preferred Azure region
azd env set AZURE_LOCATION swedencentral

# Provision infrastructure and deploy
azd up
```

### 3. View Deployed Resources

```bash
azd show
azd env get-values | grep AZURE_FUNCTION_APP_URL
```

## OpenAPI / Swagger

The API includes OpenAPI 3.0 documentation.

| URL | Description |
|-----|-------------|
| `/api/swagger/ui` | Swagger UI |
| `/api/openapi/v3.json` | OpenAPI 3.0 spec (JSON) |
| `/api/openapi/v3.yaml` | OpenAPI 3.0 spec (YAML) |

### Extract OpenAPI Spec & Generate Frontend Types

```bash
# One command to extract spec and generate frontend types
./scripts/extract-openapi-docker.sh

# Or manually when func is running
curl http://localhost:7071/api/openapi/v3.json > openapi.json
```

The frontend (`doclens-app`) uses RTK Query with automatic code generation from the OpenAPI spec for fully typed API hooks.

## Development Principles

This project follows:

- **TDD** - Tests written before implementation
- **SOLID** - Single responsibility, interface segregation, dependency injection
- **Clean Architecture** - Thin functions, business logic in services

## Next Steps

- [x] Azure Blob Storage for PDF storage
- [x] Azure Document Intelligence for PDF parsing
- [x] Azure OpenAI embeddings + chat
- [x] Azure AI Search for vector search
- [x] SSE streaming for indexing progress
- [x] SSE streaming for Q&A responses
- [x] Chat history with session management
- [x] Rate limit handling with exponential backoff
- [x] OpenAPI/Swagger documentation
- [x] Frontend type-safe API generation (RTK Query codegen)
- [ ] Authentication (Azure AD B2C or similar)
- [ ] Multi-document search (search across all user documents)
- [ ] Document sharing between users
- [ ] Usage quotas and billing
