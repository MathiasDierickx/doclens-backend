# DocLens AI Architecture

## Overview

RAG (Retrieval-Augmented Generation) pipeline for document Q&A. Fully serverless on Azure.

## Key Features

- **PDF highlighting**: Bounding box coordinates preserved from extraction → chunking → search → frontend
- **Chat sessions**: Follow-up questions with conversation history
- **SSE streaming**: Real-time token-by-token response streaming
- **Source references**: Click-to-navigate with PDF region highlighting

## Tech Stack

### Document Processing

| Component | Service | Why |
|-----------|---------|-----|
| **PDF Text Extraction** | Azure AI Document Intelligence | Extracts text with **bounding boxes** (paragraph coordinates). Preserves page numbers and positions for PDF highlighting. Serverless pay-per-page. |
| **Chunking** | Custom (.NET) | Split into chunks (~2000 chars) with overlap. Preserves `TextPosition` data (page, bounding box, char offset) per chunk. |

### Vector Storage & Search

| Component | Service | Why |
|-----------|---------|-----|
| **Vector Database** | Azure AI Search | Native vector search with hybrid (keyword + semantic) support. Serverless tier available. Integrated with Azure ecosystem. |
| **Embeddings** | Azure OpenAI (text-embedding-3-small) | Generate embeddings for chunks. 1536 dimensions, good quality/cost balance. |

### Q&A Generation

| Component | Service | Why |
|-----------|---------|-----|
| **LLM** | Azure OpenAI (gpt-4o-mini) | Fast, cheap, good for RAG. Streaming responses via SSE. |
| **Chat Sessions** | In-memory (IChatSessionService) | Maintains conversation history for follow-up questions. Session ID returned in SSE `done` event. |
| **Orchestration** | Azure Functions | Serverless, scales to zero, blob triggers for indexing. |

## Architecture Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                         INDEXING PIPELINE                           │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────┐    ┌─────────────────┐    ┌──────────────────┐       │
│  │  Blob    │───▶│ Document        │───▶│ Chunking         │       │
│  │  Upload  │    │ Intelligence    │    │ (with metadata)  │       │
│  └──────────┘    └─────────────────┘    └────────┬─────────┘       │
│                                                   │                 │
│                                                   ▼                 │
│                                          ┌──────────────────┐       │
│                                          │ Azure OpenAI     │       │
│                                          │ Embeddings       │       │
│                                          └────────┬─────────┘       │
│                                                   │                 │
│                                                   ▼                 │
│                                          ┌──────────────────┐       │
│                                          │ Azure AI Search  │       │
│                                          │ (Vector Index)   │       │
│                                          └──────────────────┘       │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                          QUERY PIPELINE                             │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────┐    ┌─────────────────┐    ┌──────────────────┐       │
│  │  User    │───▶│ Embed Query     │───▶│ Vector Search    │       │
│  │  Query   │    │ (OpenAI)        │    │ (AI Search)      │       │
│  └──────────┘    └─────────────────┘    └────────┬─────────┘       │
│                                                   │                 │
│                                                   ▼                 │
│                                          ┌──────────────────┐       │
│                                          │ LLM Generation   │       │
│                                          │ (gpt-4o-mini)    │       │
│                                          │ + source refs    │       │
│                                          └────────┬─────────┘       │
│                                                   │                 │
│                                                   ▼                 │
│                                          ┌──────────────────┐       │
│                                          │ SSE Stream:      │       │
│                                          │ • chunks (text)  │       │
│                                          │ • sources (refs) │       │
│                                          │ • done (session) │       │
│                                          └──────────────────┘       │
└─────────────────────────────────────────────────────────────────────┘
```

## Azure Resources

### New Resources to Add

```
Azure AI Document Intelligence (S0)
├── Pay-per-page pricing
├── ~$1.50 per 1000 pages (prebuilt-read)
└── Extracts: text, tables, paragraphs, page numbers

Azure AI Search (Free or Basic tier)
├── Free: 50MB storage, 3 indexes
├── Basic: 2GB storage, 15 indexes (~$70/month)
└── Vector search + hybrid search

Azure OpenAI Service
├── text-embedding-3-small: $0.02 per 1M tokens
├── gpt-4o-mini: $0.15/$0.60 per 1M tokens (input/output)
└── Region: Sweden Central or East US (best availability)
```

### Cost Estimate (Low Usage)

| Service | Usage | Cost/month |
|---------|-------|------------|
| Document Intelligence | 100 pages | ~$0.15 |
| Azure OpenAI Embeddings | 500K tokens | ~$0.01 |
| Azure OpenAI GPT-4o-mini | 1M tokens | ~$0.75 |
| Azure AI Search | Free tier | $0 |
| **Total** | | **~$1/month** |

## Implementation Plan

### Phase 1: Indexing Pipeline

1. **Blob Trigger Function** - Triggered when PDF uploaded to `documents` container
2. **Document Intelligence** - Extract text with page numbers
3. **Chunking** - Split into ~500 token chunks, preserve page metadata
4. **Embedding** - Generate vectors via Azure OpenAI
5. **Index** - Store in Azure AI Search with metadata (documentId, page, text, vector)

### Phase 2: Query Pipeline

1. **Query Endpoint** - `POST /api/documents/{id}/ask`
2. **Embed Query** - Convert user question to vector
3. **Search** - Hybrid search (vector + keyword) filtered by documentId
4. **Generate** - Pass top-k chunks + question to GPT-4o-mini
5. **Response** - Return answer with source page references

## Data Schema

### AI Search Index

```json
{
  "name": "documents-index",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "documentId", "type": "Edm.String", "filterable": true },
    { "name": "chunkIndex", "type": "Edm.Int32" },
    { "name": "pageNumber", "type": "Edm.Int32", "filterable": true },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536, "vectorSearchProfile": "default" },
    { "name": "positionsJson", "type": "Edm.String" }
  ]
}
```

### Position Data (for PDF highlighting)

Each chunk stores `positionsJson` containing paragraph bounding boxes:

```typescript
// TextPosition (per paragraph in chunk)
{
  pageNumber: number,
  boundingBox: {
    x: number,      // inches from left
    y: number,      // inches from top
    width: number,  // inches
    height: number  // inches
  },
  charOffset: number,
  charLength: number
}
```

### SSE Response Format

The `/api/documents/{id}/ask` endpoint streams Server-Sent Events:

```
event: chunk
data: {"content": "The document states..."}

event: chunk
data: {"content": " that the main finding..."}

event: sources
data: {"sources": [
  {
    "page": 3,
    "text": "Preview of source text...",
    "positions": [{ "pageNumber": 3, "boundingBox": {...}, ... }]
  }
]}

event: done
data: {"sessionId": "abc-123"}
```

## Alternatives Considered

| Option | Pros | Cons | Decision |
|--------|------|------|----------|
| **Pinecone** | Great vector DB | Extra vendor, cost | Use AI Search (Azure native) |
| **pgvector** | Open source | Need managed Postgres | Use AI Search (serverless) |
| **LangChain** | Popular framework | Overkill for simple RAG | Custom implementation |
| **Semantic Kernel** | .NET native, Microsoft | Good option | Consider for orchestration |

## Environment Variables

```
# Azure AI Document Intelligence
DOCUMENT_INTELLIGENCE_ENDPOINT=https://<name>.cognitiveservices.azure.com/
DOCUMENT_INTELLIGENCE_KEY=<key>

# Azure OpenAI
AZURE_OPENAI_ENDPOINT=https://<name>.openai.azure.com/
AZURE_OPENAI_KEY=<key>
AZURE_OPENAI_EMBEDDING_DEPLOYMENT=text-embedding-3-small
AZURE_OPENAI_CHAT_DEPLOYMENT=gpt-4o-mini

# Azure AI Search
AZURE_SEARCH_ENDPOINT=https://<name>.search.windows.net
AZURE_SEARCH_KEY=<key>
AZURE_SEARCH_INDEX=documents-index

# Azure Blob Storage
StorageConnection=<connection-string>
DocumentsContainer=documents
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/documents/upload-url` | POST | Get SAS URL for PDF upload |
| `/api/documents/{id}/download-url` | GET | Get SAS URL for PDF download (15 min expiry) |
| `/api/documents/{id}/ask` | POST | Ask question, returns SSE stream |
| `/api/documents/{id}/status` | GET | Get indexing status |
| `/api/documents` | GET | List all documents |
| `/api/documents/{id}` | GET | Get document details |
| `/api/documents/{id}` | DELETE | Delete document |

## Frontend Integration

### PDF Highlighting Flow

1. User asks question → backend returns `sources` with `positions`
2. User clicks source card in chat
3. Frontend converts bounding box (inches) → percentage of page:
   ```typescript
   left: (boundingBox.x / pageWidth) * 100
   top: (boundingBox.y / pageHeight) * 100
   ```
4. PDF viewer highlights the region and navigates to page

### Chat Session Flow

1. First question → backend creates session, returns `sessionId` in `done` event
2. Frontend stores `sessionId`
3. Follow-up questions include `sessionId` in request body
4. Backend includes conversation history in LLM context
