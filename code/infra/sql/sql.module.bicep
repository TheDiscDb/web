@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource sqlServerAdminManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: take('sql-admin-${uniqueString(resourceGroup().id)}', 63)
  location: location
}

resource sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'thediscdb'
  location: location
  kind: 'v12.0'
  properties: {
    administratorLogin: 'thediscdb'
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
  }
  tags: {
    'aspire-resource-name': 'sql'
  }
}

resource sqlFirewallRule_AllowAllAzureIps 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  name: 'AllowAllAzureIps'
  properties: {
    endIpAddress: '0.0.0.0'
    startIpAddress: '0.0.0.0'
  }
  parent: sql
}

resource sqldatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sql
  name: 'thediscdb'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
    capacity: 20
  }
  kind: 'v12.0,user'
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 1073741824
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: 'Geo'
    isLedgerOn: false
    availabilityZone: 'NoPreference'
  }
}


output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName

output name string = sql.name

output sqlServerAdminName string = sqlServerAdminManagedIdentity.name
