# DocLens Backend

Document Q&A API powered by Azure Functions and Azure AI Services.

## Architecture

- **Runtime**: .NET 8 Azure Functions (Isolated Worker)
- **Hosting**: Azure Functions Consumption Plan (serverless)
- **Infrastructure**: Bicep + Azure Developer CLI (azd)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
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

## Deployment to Azure

### 1. Install Azure Developer CLI

```bash
# macOS
brew tap azure/azd && brew install azd

# Windows
winget install microsoft.azd

# Linux
curl -fsSL https://aka.ms/install-azd.sh | bash
```

### 2. Login to Azure

```bash
azd auth login
```

### 3. Initialize and deploy

```bash
# Initialize environment (first time only)
azd init

# Provision infrastructure and deploy
azd up
```

This will:
- Create a resource group
- Deploy Azure Functions (Consumption plan)
- Deploy Application Insights
- Deploy Storage Account
- Deploy your code

### 4. View deployed resources

```bash
azd show
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
        │   └── HelloFunction.cs
        └── Program.cs
```

## Next Steps

- [ ] Add CosmosDB for document metadata
- [ ] Add Azure Blob Storage for PDF storage
- [ ] Integrate Azure Document Intelligence for PDF parsing
- [ ] Integrate Azure OpenAI for Q&A
- [ ] Add OpenAPI/Swagger documentation
