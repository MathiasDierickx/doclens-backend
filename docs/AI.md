# DocLens AI Architecture

## Overview

RAG (Retrieval-Augmented Generation) pipeline for document Q&A. Fully serverless on Azure.

## Tech Stack

### Document Processing

| Component | Service | Why |
|-----------|---------|-----|
| **PDF Text Extraction** | Azure AI Document Intelligence | Extracts text, tables, and structure from PDFs. Preserves page numbers for source references. Serverless pay-per-page pricing. |
| **Chunking** | Custom (.NET) | Split extracted text into chunks (~500-1000 tokens) with overlap. Keep page metadata per chunk. |

### Vector Storage & Search

| Component | Service | Why |
|-----------|---------|-----|
| **Vector Database** | Azure AI Search | Native vector search with hybrid (keyword + semantic) support. Serverless tier available. Integrated with Azure ecosystem. |
| **Embeddings** | Azure OpenAI (text-embedding-3-small) | Generate embeddings for chunks. 1536 dimensions, good quality/cost balance. |

### Q&A Generation

| Component | Service | Why |
|-----------|---------|-----|
| **LLM** | Azure OpenAI (gpt-4o-mini) | Fast, cheap, good for RAG. Falls back to gpt-4o for complex queries if needed. |
| **Orchestration** | Azure Functions | Serverless, scales to zero, integrates with blob triggers. |

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
│                                          │ Response with    │       │
│                                          │ page citations   │       │
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
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536, "vectorSearchProfile": "default" }
  ]
}
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
```
