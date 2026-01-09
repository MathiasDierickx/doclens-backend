targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment (e.g., dev, prod)')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Name of the resource group')
param resourceGroupName string = ''

@description('Name of the function app')
param functionAppName string = ''

@description('Name of the storage account')
param storageAccountName string = ''

// Tags that should be applied to all resources
var tags = {
  'azd-env-name': environmentName
  'project': 'doclens'
}

// Generate unique suffix based on subscription and environment
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))

// Resource group
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: !empty(resourceGroupName) ? resourceGroupName : 'rg-doclens-${environmentName}'
  location: location
  tags: tags
}

// Function App module
module functionApp 'modules/function-app.bicep' = {
  name: 'functionApp'
  scope: rg
  params: {
    location: location
    tags: tags
    functionAppName: !empty(functionAppName) ? functionAppName : 'func-doclens-${resourceToken}'
    storageAccountName: !empty(storageAccountName) ? storageAccountName : 'stdoclens${resourceToken}'
  }
}

// Outputs for azd
output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_FUNCTION_APP_NAME string = functionApp.outputs.functionAppName
output AZURE_FUNCTION_APP_URL string = functionApp.outputs.functionAppUrl
