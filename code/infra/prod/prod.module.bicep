@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource prod_asplan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'thediscdb'
  location: location
  sku: {
    name: 'P0v3'
    tier: 'Premium0V3'
    size: 'P0v3'
    family: 'Pv3'
    capacity: 1
  }
  kind: 'app'
  properties: {
    perSiteScaling: false
    elasticScaleEnabled: false
    maximumElasticWorkerCount: 1
    isSpot: false
    reserved: false
    isXenon: false
    hyperV: false
    targetWorkerCount: 0
    targetWorkerSizeId: 0
    zoneRedundant: false
    asyncScalingEnabled: false
  }
}

output name string = prod_asplan.name

output planId string = prod_asplan.id
