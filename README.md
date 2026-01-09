# DocLens Backend

Document Q&A API powered by Azure Functions and Azure AI Services.

## Architecture

- **Runtime**: .NET 9 Azure Functions (Isolated Worker)
- **Hosting**: Azure Functions Consumption Plan (serverless)
- **Infrastructure**: Bicep + Azure Developer CLI (azd)

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/azure/azure-functions/functions-run-local)
- [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) (for authentication)

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
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
```

### 3. Run locally

```bash
cd src/DocLens.Api
func start
```

The API will be available at `http://localhost:7071`.

### Available Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check |
| `/api/hello?name=Your Name` | GET | Hello world test |
| `/api/documents/upload-url?filename=doc.pdf` | POST | Get SAS URL for direct upload |
| `/api/documents` | GET | List all uploaded documents |
| `/api/documents/{documentId}` | DELETE | Delete a document |

## Deployment to Azure

### 1. Create Azure Account

If you don't have an Azure account, create one at [azure.microsoft.com/free](https://azure.microsoft.com/free).
You get €170 free credit for the first 30 days.

### 2. Install CLIs

```bash
# Azure CLI
brew install azure-cli

# Azure Developer CLI (azd)
brew tap azure/azd && brew install azd
```

### 3. Login to Azure

```bash
# Login to Azure CLI (needed for resource provider registration)
az login

# Login to Azure Developer CLI
azd auth login
```

### 4. Register Required Resource Providers

For a new Azure subscription, you may need to register resource providers:

```bash
az provider register --namespace microsoft.operationalinsights
az provider register --namespace Microsoft.Web
az provider register --namespace Microsoft.Storage
```

Check registration status:

```bash
az provider show --namespace microsoft.operationalinsights --query "registrationState" -o tsv
```

### 5. Initialize and Deploy

```bash
# Initialize environment (first time only - choose environment name like 'dev')
azd init

# Set your preferred Azure region (optional, will be prompted if not set)
azd env set AZURE_LOCATION belgiumcentral

# Provision infrastructure and deploy code
azd up
```

This creates:
- Resource group: `rg-doclens-{env}`
- Azure Functions (Consumption plan - serverless)
- Application Insights (monitoring)
- Storage Account

### 6. View Deployed Resources

```bash
# Show deployment info
azd show

# Get the Function App URL
azd env get-values | grep AZURE_FUNCTION_APP_URL
```

### 7. Test the Deployment

```bash
# Health check
curl https://<your-function-app>.azurewebsites.net/api/health

# Hello endpoint
curl "https://<your-function-app>.azurewebsites.net/api/hello?name=World"
```

## Project Structure

```
doclens-backend/
├── azure.yaml              # azd configuration
├── infra/                  # Bicep infrastructure
│   ├── main.bicep
│   ├── main.parameters.json
│   └── modules/
│       └── function-app.bicep
└── src/
    └── DocLens.Api/        # Azure Functions project
        ├── Functions/
        │   ├── HealthFunction.cs
        │   ├── HelloFunction.cs
        │   └── DocumentsFunction.cs
        ├── Models/
        │   └── DocumentModels.cs
        └── Program.cs
```

## OpenAPI / Swagger

The API includes OpenAPI 3.0 documentation with Swagger UI.

### Endpoints (when running)

| URL | Description |
|-----|-------------|
| `/api/swagger/ui` | Swagger UI |
| `/api/openapi/v3.json` | OpenAPI 3.0 spec (JSON) |
| `/api/openapi/v3.yaml` | OpenAPI 3.0 spec (YAML) |

### Extract OpenAPI Spec & Generate Frontend Types

The backend generates an OpenAPI 3.0 specification that is used to auto-generate fully type-safe API hooks for the frontend (RTK Query). This provides end-to-end type safety from backend to frontend.

**One command to update everything:**

```bash
./scripts/extract-openapi-docker.sh
```

This script:
1. Builds a Docker container with the .NET SDK and Azure Functions Core Tools
2. Starts the Functions app inside the container
3. Extracts the OpenAPI spec to `openapi.json`
4. If `../doclens-app` exists, copies the spec and runs `npm run codegen` to regenerate the frontend API hooks

**Alternative options:**

```bash
# Option 1: Shell script (requires func running or starts it)
./scripts/extract-openapi.sh openapi.json

# Option 2: Manual (when func is running)
curl http://localhost:7071/api/openapi/v3.json > openapi.json
```

### Frontend Type-Safe API Generation

The frontend (`doclens-app`) uses [RTK Query](https://redux-toolkit.js.org/rtk-query/overview) with automatic code generation from the OpenAPI spec. When you add or modify API endpoints:

1. Update the backend endpoint with OpenAPI attributes
2. Run `./scripts/extract-openapi-docker.sh`
3. The frontend gets fully typed hooks like `useGetHealthQuery()`, `useListDocumentsQuery()`, etc.

Example usage in frontend:
```typescript
import { useGetHealthQuery, useListDocumentsQuery } from "@/store/api/generatedApi";

function MyComponent() {
  const { data: health, isLoading } = useGetHealthQuery();
  const { data: documents } = useListDocumentsQuery();

  // data is fully typed based on the OpenAPI spec!
  return <div>{health?.status}</div>;
}
```

## Next Steps

- [ ] Add CosmosDB for document metadata
- [x] Add Azure Blob Storage for PDF storage
- [ ] Integrate Azure Document Intelligence for PDF parsing
- [ ] Integrate Azure OpenAI for Q&A
- [x] Add OpenAPI/Swagger documentation
- [x] Add frontend type-safe API generation (RTK Query codegen)
