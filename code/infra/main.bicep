targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention, the name of the resource group for your application will use this name, prefixed with rg-')
param environmentName string

@minLength(1)
@description('The location used for all deployed resources')
param location string

@description('Id of the user or app to assign application roles')
param principalId string = ''


var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module migrations_identity 'migrations-identity/migrations-identity.module.bicep' = {
  name: 'migrations-identity'
  scope: rg
  params: {
    location: location
  }
}
module migrations_roles_sql 'migrations-roles-sql/migrations-roles-sql.module.bicep' = {
  name: 'migrations-roles-sql'
  scope: rg
  params: {
    location: location
    principalId: migrations_identity.outputs.principalId
    principalName: migrations_identity.outputs.principalName
    sql_outputs_name: sql.outputs.name
    sql_outputs_sqlserveradminname: sql.outputs.sqlServerAdminName
  }
}
module migrations_roles_storage 'migrations-roles-storage/migrations-roles-storage.module.bicep' = {
  name: 'migrations-roles-storage'
  scope: rg
  params: {
    location: location
    principalId: migrations_identity.outputs.principalId
    storage_outputs_name: storage.outputs.name
  }
}
module prod 'prod/prod.module.bicep' = {
  name: 'prod'
  scope: rg
  params: {
    location: location
    userPrincipalId: principalId
  }
}
module sql 'sql/sql.module.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
  }
}
module storage 'storage/storage.module.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
  }
}
module thediscdb_web_identity 'thediscdb-web-identity/thediscdb-web-identity.module.bicep' = {
  name: 'thediscdb-web-identity'
  scope: rg
  params: {
    location: location
  }
}
module thediscdb_web_roles_sql 'thediscdb-web-roles-sql/thediscdb-web-roles-sql.module.bicep' = {
  name: 'thediscdb-web-roles-sql'
  scope: rg
  params: {
    location: location
    principalId: thediscdb_web_identity.outputs.principalId
    principalName: thediscdb_web_identity.outputs.principalName
    sql_outputs_name: sql.outputs.name
    sql_outputs_sqlserveradminname: sql.outputs.sqlServerAdminName
  }
}
module thediscdb_web_roles_storage 'thediscdb-web-roles-storage/thediscdb-web-roles-storage.module.bicep' = {
  name: 'thediscdb-web-roles-storage'
  scope: rg
  params: {
    location: location
    principalId: thediscdb_web_identity.outputs.principalId
    storage_outputs_name: storage.outputs.name
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = prod.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output MIGRATIONS_IDENTITY_CLIENTID string = migrations_identity.outputs.clientId
output MIGRATIONS_IDENTITY_ID string = migrations_identity.outputs.id
output PROD_AZURE_CONTAINER_REGISTRY_ENDPOINT string = prod.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output PROD_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_CLIENT_ID string = prod.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_CLIENT_ID
output PROD_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = prod.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID
output PROD_PLANID string = prod.outputs.planId
output SQL_SQLSERVERFQDN string = sql.outputs.sqlServerFqdn
output STORAGE_BLOBENDPOINT string = storage.outputs.blobEndpoint
output THEDISCDB_WEB_IDENTITY_CLIENTID string = thediscdb_web_identity.outputs.clientId
output THEDISCDB_WEB_IDENTITY_ID string = thediscdb_web_identity.outputs.id
