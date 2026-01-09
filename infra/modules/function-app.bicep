@description('Location for all resources')
param location string

@description('Tags to apply to resources')
param tags object

@description('Name of the function app')
param functionAppName string

@description('Name of the storage account')
param storageAccountName string

// AI Service parameters
@description('Document Intelligence endpoint')
param documentIntelligenceEndpoint string

@description('Document Intelligence key')
@secure()
param documentIntelligenceKey string

@description('Azure OpenAI endpoint')
param openAIEndpoint string

@description('Azure OpenAI key')
@secure()
param openAIKey string

@description('Azure OpenAI embedding deployment name')
param embeddingDeploymentName string

@description('Azure OpenAI chat deployment name')
param chatDeploymentName string

@description('Azure AI Search endpoint')
param searchEndpoint string

@description('Azure AI Search admin key')
@secure()
param searchAdminKey string

// Storage Account for Function App and Document Storage
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// Blob Service with CORS configuration
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    cors: {
      corsRules: [
        {
          allowedOrigins: [
            'http://localhost:3000'
            'https://doclens-app.vercel.app'
          ]
          allowedMethods: ['GET', 'PUT', 'OPTIONS', 'HEAD']
          allowedHeaders: ['*']
          exposedHeaders: ['*']
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

// Container for uploaded documents
resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'documents'
  properties: {
    publicAccess: 'None'
  }
}

// App Service Plan (Consumption)
resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${functionAppName}'
  location: location
  tags: tags
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: false // false for Windows, true for Linux
  }
}

// Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${functionAppName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Request_Source: 'rest'
    RetentionInDays: 30
  }
}

// Function App
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: union(tags, {
    'azd-service-name': 'api'
  })
  kind: 'functionapp'
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v9.0'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      cors: {
        allowedOrigins: [
          'https://portal.azure.com'
          'http://localhost:3000'
          'https://doclens-app.vercel.app'
          'https://doclens-app-*.vercel.app'
        ]
        supportCredentials: false
      }
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(functionAppName)
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'StorageConnection'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'DocumentsContainer'
          value: documentsContainer.name
        }
        // AI Services
        {
          name: 'DocumentIntelligenceEndpoint'
          value: documentIntelligenceEndpoint
        }
        {
          name: 'DocumentIntelligenceKey'
          value: documentIntelligenceKey
        }
        {
          name: 'AzureOpenAIEndpoint'
          value: openAIEndpoint
        }
        {
          name: 'AzureOpenAIKey'
          value: openAIKey
        }
        {
          name: 'AzureOpenAIEmbeddingDeployment'
          value: embeddingDeploymentName
        }
        {
          name: 'AzureOpenAIChatDeployment'
          value: chatDeploymentName
        }
        {
          name: 'AzureSearchEndpoint'
          value: searchEndpoint
        }
        {
          name: 'AzureSearchKey'
          value: searchAdminKey
        }
        {
          name: 'AzureSearchIndexName'
          value: 'documents-index'
        }
      ]
    }
  }
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output storageAccountName string = storageAccount.name
output documentsContainerName string = documentsContainer.name
