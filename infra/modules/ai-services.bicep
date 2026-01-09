@description('Location for all resources')
param location string

@description('Tags to apply to resources')
param tags object

@description('Unique suffix for resource names')
param resourceToken string

@description('Azure OpenAI location (Sweden Central for text-embedding-3-small availability)')
param openAILocation string = 'swedencentral'

// Document Intelligence (Form Recognizer)
resource documentIntelligence 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: 'di-doclens-${resourceToken}'
  location: location
  tags: tags
  kind: 'FormRecognizer'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: 'di-doclens-${resourceToken}'
    publicNetworkAccess: 'Enabled'
  }
}

// Azure OpenAI
resource openAI 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: 'oai-doclens-${resourceToken}'
  location: openAILocation
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: 'oai-doclens-${resourceToken}'
    publicNetworkAccess: 'Enabled'
  }
}

// Azure OpenAI Embedding Deployment
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  parent: openAI
  name: 'text-embedding-3-small'
  sku: {
    name: 'Standard'
    capacity: 120 // 120K tokens per minute
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-small'
      version: '1'
    }
  }
}

// Azure OpenAI Chat Deployment
resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  parent: openAI
  name: 'gpt-4o-mini'
  dependsOn: [embeddingDeployment] // Deploy sequentially to avoid rate limits
  sku: {
    name: 'Standard'
    capacity: 30 // 30K tokens per minute
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: '2024-07-18'
    }
  }
}

// Azure AI Search
resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: 'srch-doclens-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'free' // Free tier: 50MB storage, 3 indexes
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'disabled' // Not available on free tier
  }
}

// Table Storage for indexing job status (uses existing storage account)
// This is configured in function-app.bicep

// Outputs
output documentIntelligenceEndpoint string = documentIntelligence.properties.endpoint
output documentIntelligenceKey string = documentIntelligence.listKeys().key1

output openAIEndpoint string = openAI.properties.endpoint
output openAIKey string = openAI.listKeys().key1
output embeddingDeploymentName string = embeddingDeployment.name
output chatDeploymentName string = chatDeployment.name

output searchEndpoint string = 'https://${searchService.name}.search.windows.net'
output searchAdminKey string = searchService.listAdminKeys().primaryKey
output searchServiceName string = searchService.name
