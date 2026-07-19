targetScope = 'resourceGroup'

@description('Azure region for all regional resources.')
param location string = resourceGroup().location

@description('Lowercase environment name used to derive resource names. Use letters, numbers, and hyphens only.')
param environmentName string = 'workreservationweb'

@description('Daily reminder NCRONTAB expression including seconds.')
param reservationReminderSchedule string = '0 0 0 * * *'

@description('Cosmos DB SQL database name used by the application.')
param cosmosDatabaseName string = 'WorkReservationWeb'

@description('Cosmos DB SQL container name used by the application.')
param cosmosContainerName string = 'Reservations'

@description('Enable the Cosmos DB free tier. Azure allows one free-tier Cosmos DB account per subscription.')
param enableCosmosFreeTier bool = true

@description('Blob container name used for service offer images.')
param blobStorageContainerName string = 'service-offer-images'

@secure()
@description('Optional Azure Communication Services connection string. Leave empty to use the local notification fallback.')
param communicationServicesConnectionString string = ''

@description('Optional Azure Communication Services sender address. Leave empty to use the local notification fallback.')
param communicationServicesSenderAddress string = ''

var compactEnvironmentName = take(toLower(replace(environmentName, '-', '')), 14)
var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id, environmentName)
var storageAccountName = take('${compactEnvironmentName}${uniqueSuffix}', 24)
var staticWebAppName = take('${environmentName}-swa-${uniqueSuffix}', 60)
var functionAppName = take('${environmentName}-reminders-${uniqueSuffix}', 60)
var functionPlanName = take('${environmentName}-reminders-plan-${uniqueSuffix}', 40)
var appInsightsName = take('${environmentName}-appi-${uniqueSuffix}', 90)
var logAnalyticsWorkspaceName = take('${environmentName}-log-${uniqueSuffix}', 63)
var cosmosAccountName = take('${environmentName}-cosmos-${uniqueSuffix}', 44)
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var cosmosConnectionString = cosmosAccount.listConnectionStrings().connectionStrings[0].connectionString

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource serviceOfferImagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: blobStorageContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: cosmosAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: enableCosmosFreeTier
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: cosmosDatabaseName
  properties: {
    resource: {
      id: cosmosDatabaseName
    }
    options: {
      throughput: 400
    }
  }
}

resource cosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: cosmosDatabase
  name: cosmosContainerName
  properties: {
    resource: {
      id: cosmosContainerName
      partitionKey: {
        kind: 'Hash'
        paths: [
          '/partitionKey'
        ]
      }
    }
  }
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

resource functionPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: functionPlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {}
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      netFrameworkVersion: 'v9.0'
      use32BitWorkerProcess: false
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ReservationReminderSchedule'
          value: reservationReminderSchedule
        }
        {
          name: 'CosmosDb__ConnectionString'
          value: cosmosConnectionString
        }
        {
          name: 'CosmosDb__DatabaseName'
          value: cosmosDatabaseName
        }
        {
          name: 'CosmosDb__ContainerName'
          value: cosmosContainerName
        }
        {
          name: 'CommunicationServices__ConnectionString'
          value: communicationServicesConnectionString
        }
        {
          name: 'CommunicationServices__SenderAddress'
          value: communicationServicesSenderAddress
        }
      ]
    }
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output staticWebAppName string = staticWebApp.name
output staticWebAppDefaultHostname string = staticWebApp.properties.defaultHostname
output functionAppName string = functionApp.name
output cosmosAccountName string = cosmosAccount.name
output storageAccountName string = storageAccount.name
output serviceOfferImagesContainerName string = serviceOfferImagesContainer.name
output applicationInsightsConnectionString string = appInsights.properties.ConnectionString
