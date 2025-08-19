@secure()
param vulnerabilityAssessments_Default_storageContainerPath string
param sites_discdb_name string = 'discdb'
param servers_thediscdb_name string = 'thediscdb'
param serverfarms_thediscdb_name string = 'thediscdb'
param searchServices_thediscdb_name string = 'thediscdb'
param storageAccounts_discdbstatic_name string = 'discdbstatic'
param storageAccounts_searchindexer_name string = 'searchindexer'

resource searchServices_thediscdb_name_resource 'Microsoft.Search/searchServices@2025-05-01' = {
  name: searchServices_thediscdb_name
  location: 'West US 2'
  sku: {
    name: 'free'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    endpoint: 'https://${searchServices_thediscdb_name}.search.windows.net'
    hostingMode: 'Default'
    publicNetworkAccess: 'Enabled'
    networkRuleSet: {
      ipRules: []
      bypass: 'None'
    }
    encryptionWithCmk: {}
    disableLocalAuth: false
    authOptions: {
      apiKeyOnly: {}
    }
    dataExfiltrationProtections: []
    semanticSearch: 'disabled'
    upgradeAvailable: 'notAvailable'
  }
}

resource servers_thediscdb_name_resource 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: servers_thediscdb_name
  location: 'westus'
  kind: 'v12.0'
  properties: {
    administratorLogin: servers_thediscdb_name
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
  }
}

resource storageAccounts_discdbstatic_name_resource 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: storageAccounts_discdbstatic_name
  location: 'westus'
  sku: {
    name: 'Standard_RAGRS'
    tier: 'Standard'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: true
    allowSharedKeyAccess: true
    networkAcls: {
      bypass: 'AzureServices'
      virtualNetworkRules: []
      ipRules: []
      defaultAction: 'Allow'
    }
    supportsHttpsTrafficOnly: true
    encryption: {
      services: {
        file: {
          keyType: 'Account'
          enabled: true
        }
        blob: {
          keyType: 'Account'
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
    accessTier: 'Hot'
    customDomain: {
      name: 'static.thediscdb.com'
    }
  }
}

resource storageAccounts_searchindexer_name_resource 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: storageAccounts_searchindexer_name
  location: 'westus'
  sku: {
    name: 'Standard_LRS'
    tier: 'Standard'
  }
  kind: 'Storage'
  properties: {
    minimumTlsVersion: 'TLS1_0'
    allowBlobPublicAccess: true
    networkAcls: {
      bypass: 'AzureServices'
      virtualNetworkRules: []
      ipRules: []
      defaultAction: 'Allow'
    }
    supportsHttpsTrafficOnly: true
    encryption: {
      services: {
        file: {
          keyType: 'Account'
          enabled: true
        }
        blob: {
          keyType: 'Account'
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
  }
}

resource serverfarms_thediscdb_name_resource 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: serverfarms_thediscdb_name
  location: 'West US'
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

resource servers_thediscdb_name_Default 'Microsoft.Sql/servers/advancedThreatProtectionSettings@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'Default'
  properties: {
    state: 'Disabled'
  }
}

resource servers_thediscdb_name_CreateIndex 'Microsoft.Sql/servers/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_resource
  name: 'CreateIndex'
  properties: {
    autoExecuteValue: 'Disabled'
  }
}

resource servers_thediscdb_name_DbParameterization 'Microsoft.Sql/servers/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_resource
  name: 'DbParameterization'
  properties: {
    autoExecuteValue: 'Disabled'
  }
}

resource servers_thediscdb_name_DefragmentIndex 'Microsoft.Sql/servers/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_resource
  name: 'DefragmentIndex'
  properties: {
    autoExecuteValue: 'Disabled'
  }
}

resource servers_thediscdb_name_DropIndex 'Microsoft.Sql/servers/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_resource
  name: 'DropIndex'
  properties: {
    autoExecuteValue: 'Disabled'
  }
}

resource servers_thediscdb_name_ForceLastGoodPlan 'Microsoft.Sql/servers/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_resource
  name: 'ForceLastGoodPlan'
  properties: {
    autoExecuteValue: 'Enabled'
  }
}

resource Microsoft_Sql_servers_auditingPolicies_servers_thediscdb_name_Default 'Microsoft.Sql/servers/auditingPolicies@2014-04-01' = {
  parent: servers_thediscdb_name_resource
  name: 'Default'
  location: 'West US'
  properties: {
    auditingState: 'Disabled'
  }
}

resource Microsoft_Sql_servers_auditingSettings_servers_thediscdb_name_Default 'Microsoft.Sql/servers/auditingSettings@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'default'
  properties: {
    retentionDays: 0
    auditActionsAndGroups: []
    isStorageSecondaryKeyInUse: false
    isAzureMonitorTargetEnabled: false
    isManagedIdentityInUse: false
    state: 'Disabled'
    storageAccountSubscriptionId: '00000000-0000-0000-0000-000000000000'
  }
}

resource Microsoft_Sql_servers_connectionPolicies_servers_thediscdb_name_default 'Microsoft.Sql/servers/connectionPolicies@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'default'
  location: 'westus'
  properties: {
    connectionType: 'Redirect'
  }
}

resource servers_thediscdb_name_servers_thediscdb_name 'Microsoft.Sql/servers/databases@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: '${servers_thediscdb_name}'
  location: 'westus'
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
    maintenanceConfigurationId: '/subscriptions/59a15238-187b-492e-8301-a40f5731b49a/providers/Microsoft.Maintenance/publicMaintenanceConfigurations/SQL_Default'
    isLedgerOn: false
    availabilityZone: 'NoPreference'
  }
}

resource servers_thediscdb_name_master_Default 'Microsoft.Sql/servers/databases/advancedThreatProtectionSettings@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Default'
  properties: {
    state: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_auditingPolicies_servers_thediscdb_name_master_Default 'Microsoft.Sql/servers/databases/auditingPolicies@2014-04-01' = {
  name: '${servers_thediscdb_name}/master/Default'
  location: 'West US'
  properties: {
    auditingState: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_auditingSettings_servers_thediscdb_name_master_Default 'Microsoft.Sql/servers/databases/auditingSettings@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Default'
  properties: {
    retentionDays: 0
    isAzureMonitorTargetEnabled: false
    state: 'Disabled'
    storageAccountSubscriptionId: '00000000-0000-0000-0000-000000000000'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_extendedAuditingSettings_servers_thediscdb_name_master_Default 'Microsoft.Sql/servers/databases/extendedAuditingSettings@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Default'
  properties: {
    retentionDays: 0
    isAzureMonitorTargetEnabled: false
    state: 'Disabled'
    storageAccountSubscriptionId: '00000000-0000-0000-0000-000000000000'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_geoBackupPolicies_servers_thediscdb_name_master_Default 'Microsoft.Sql/servers/databases/geoBackupPolicies@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Default'
  properties: {
    state: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource servers_thediscdb_name_master_Current 'Microsoft.Sql/servers/databases/ledgerDigestUploads@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Current'
  properties: {}
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_securityAlertPolicies_servers_thediscdb_name_master_Default 'Microsoft.Sql/servers/databases/securityAlertPolicies@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Default'
  properties: {
    state: 'Disabled'
    disabledAlerts: [
      ''
    ]
    emailAddresses: [
      ''
    ]
    emailAccountAdmins: false
    retentionDays: 0
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_transparentDataEncryption_servers_thediscdb_name_master_Current 'Microsoft.Sql/servers/databases/transparentDataEncryption@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Current'
  properties: {
    state: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_vulnerabilityAssessments_servers_thediscdb_name_master_Default 'Microsoft.Sql/servers/databases/vulnerabilityAssessments@2024-05-01-preview' = {
  name: '${servers_thediscdb_name}/master/Default'
  properties: {
    recurringScans: {
      isEnabled: false
      emailSubscriptionAdmins: true
    }
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_devOpsAuditingSettings_servers_thediscdb_name_Default 'Microsoft.Sql/servers/devOpsAuditingSettings@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'Default'
  properties: {
    isAzureMonitorTargetEnabled: false
    isManagedIdentityInUse: false
    state: 'Disabled'
    storageAccountSubscriptionId: '00000000-0000-0000-0000-000000000000'
  }
}

resource servers_thediscdb_name_current 'Microsoft.Sql/servers/encryptionProtector@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'current'
  kind: 'servicemanaged'
  properties: {
    serverKeyName: 'ServiceManaged'
    serverKeyType: 'ServiceManaged'
    autoRotationEnabled: false
  }
}

resource Microsoft_Sql_servers_extendedAuditingSettings_servers_thediscdb_name_Default 'Microsoft.Sql/servers/extendedAuditingSettings@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'default'
  properties: {
    retentionDays: 0
    auditActionsAndGroups: []
    isStorageSecondaryKeyInUse: false
    isAzureMonitorTargetEnabled: false
    isManagedIdentityInUse: false
    state: 'Disabled'
    storageAccountSubscriptionId: '00000000-0000-0000-0000-000000000000'
  }
}

resource servers_thediscdb_name_AllowAllWindowsAzureIps 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource servers_thediscdb_name_ClientIPAddress_2022_5_25_10_26_8 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'ClientIPAddress_2022-5-25_10-26-8'
  properties: {
    startIpAddress: '66.235.6.0'
    endIpAddress: '66.235.6.255'
  }
}

resource servers_thediscdb_name_ClientIPAddress_2022_9_26_20_14_11 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'ClientIPAddress_2022-9-26_20-14-11'
  properties: {
    startIpAddress: '66.235.2.0'
    endIpAddress: '66.235.2.255'
  }
}

resource servers_thediscdb_name_ClientIPAddress_2024_3_6_13_48_51 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'ClientIPAddress_2024-3-6_13-48-51'
  properties: {
    startIpAddress: '66.235.0.0'
    endIpAddress: '66.235.0.255'
  }
}

resource servers_thediscdb_name_ClientIPAddress_2024_4_13_11_28_30 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'ClientIPAddress_2024-4-13_11-28-30'
  properties: {
    startIpAddress: '66.235.12.0'
    endIpAddress: '66.235.12.255'
  }
}

resource servers_thediscdb_name_ClientIPAddress_2024_4_18_19_15_2 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'ClientIPAddress_2024-4-18_19-15-2'
  properties: {
    startIpAddress: '207.68.128.0'
    endIpAddress: '207.68.128.255'
  }
}

resource servers_thediscdb_name_ClientIPAddress_2024_8_17_11_28_59 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'ClientIPAddress_2024-8-17_11-28-59'
  properties: {
    startIpAddress: '72.234.28.0'
    endIpAddress: '72.234.28.255'
  }
}

resource servers_thediscdb_name_Work 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'Work'
  properties: {
    startIpAddress: '131.107.147.0'
    endIpAddress: '131.107.147.255'
  }
}

resource servers_thediscdb_name_vac1 'Microsoft.Sql/servers/ipv6FirewallRules@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'vac1'
  properties: {
    startIPv6Address: '2603:800c:200f:f3f3:382b:ea6a:92ce:1fff'
    endIPv6Address: '2603:800c:200f:f3f3:382b:ea6a:92ce:1fff'
  }
}

resource servers_thediscdb_name_ServiceManaged 'Microsoft.Sql/servers/keys@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'ServiceManaged'
  kind: 'servicemanaged'
  properties: {
    serverKeyType: 'ServiceManaged'
  }
}

resource Microsoft_Sql_servers_securityAlertPolicies_servers_thediscdb_name_Default 'Microsoft.Sql/servers/securityAlertPolicies@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'Default'
  properties: {
    state: 'Disabled'
    disabledAlerts: [
      ''
    ]
    emailAddresses: [
      ''
    ]
    emailAccountAdmins: false
    retentionDays: 0
  }
}

resource Microsoft_Sql_servers_sqlVulnerabilityAssessments_servers_thediscdb_name_Default 'Microsoft.Sql/servers/sqlVulnerabilityAssessments@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'Default'
  properties: {
    state: 'Disabled'
  }
}

resource Microsoft_Sql_servers_vulnerabilityAssessments_servers_thediscdb_name_Default 'Microsoft.Sql/servers/vulnerabilityAssessments@2024-05-01-preview' = {
  parent: servers_thediscdb_name_resource
  name: 'Default'
  properties: {
    recurringScans: {
      isEnabled: false
      emailSubscriptionAdmins: true
    }
    storageContainerPath: vulnerabilityAssessments_Default_storageContainerPath
  }
}

resource storageAccounts_discdbstatic_name_default 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_resource
  name: 'default'
  sku: {
    name: 'Standard_RAGRS'
    tier: 'Standard'
  }
  properties: {
    changeFeed: {
      enabled: false
    }
    restorePolicy: {
      enabled: false
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    cors: {
      corsRules: []
    }
    deleteRetentionPolicy: {
      allowPermanentDelete: false
      enabled: true
      days: 7
    }
    isVersioningEnabled: false
  }
}

resource storageAccounts_searchindexer_name_default 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storageAccounts_searchindexer_name_resource
  name: 'default'
  sku: {
    name: 'Standard_LRS'
    tier: 'Standard'
  }
  properties: {
    cors: {
      corsRules: []
    }
    deleteRetentionPolicy: {
      allowPermanentDelete: false
      enabled: false
    }
  }
}

resource Microsoft_Storage_storageAccounts_fileServices_storageAccounts_discdbstatic_name_default 'Microsoft.Storage/storageAccounts/fileServices@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_resource
  name: 'default'
  sku: {
    name: 'Standard_RAGRS'
    tier: 'Standard'
  }
  properties: {
    protocolSettings: {
      smb: {}
    }
    cors: {
      corsRules: []
    }
    shareDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource Microsoft_Storage_storageAccounts_fileServices_storageAccounts_searchindexer_name_default 'Microsoft.Storage/storageAccounts/fileServices@2025-01-01' = {
  parent: storageAccounts_searchindexer_name_resource
  name: 'default'
  sku: {
    name: 'Standard_LRS'
    tier: 'Standard'
  }
  properties: {
    protocolSettings: {
      smb: {}
    }
    cors: {
      corsRules: []
    }
    shareDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource Microsoft_Storage_storageAccounts_queueServices_storageAccounts_discdbstatic_name_default 'Microsoft.Storage/storageAccounts/queueServices@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_resource
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
  }
}

resource Microsoft_Storage_storageAccounts_queueServices_storageAccounts_searchindexer_name_default 'Microsoft.Storage/storageAccounts/queueServices@2025-01-01' = {
  parent: storageAccounts_searchindexer_name_resource
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
  }
}

resource Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default 'Microsoft.Storage/storageAccounts/tableServices@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_resource
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
  }
}

resource Microsoft_Storage_storageAccounts_tableServices_storageAccounts_searchindexer_name_default 'Microsoft.Storage/storageAccounts/tableServices@2025-01-01' = {
  parent: storageAccounts_searchindexer_name_resource
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
  }
}

resource sites_discdb_name_resource 'Microsoft.Web/sites@2024-11-01' = {
  name: sites_discdb_name
  location: 'West US'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/59a15238-187b-492e-8301-a40f5731b49a/resourceGroups/thediscdb/providers/microsoft.insights/components/discdb'
  }
  kind: 'app'
  properties: {
    enabled: true
    hostNameSslStates: [
      {
        name: '${sites_discdb_name}.azurewebsites.net'
        sslState: 'Disabled'
        hostType: 'Standard'
      }
      {
        name: 'thechapterdb.com'
        sslState: 'SniEnabled'
        thumbprint: '87241FF71753D743C435BFE8B6AAE8ACC1C17361'
        hostType: 'Standard'
      }
      {
        name: 'the${sites_discdb_name}.com'
        sslState: 'SniEnabled'
        thumbprint: 'A1EF143192E1854EBAB5F97A1E4B3A98F32AE327'
        hostType: 'Standard'
      }
      {
        name: '${sites_discdb_name}.scm.azurewebsites.net'
        sslState: 'Disabled'
        hostType: 'Repository'
      }
    ]
    serverFarmId: serverfarms_thediscdb_name_resource.id
    reserved: false
    isXenon: false
    hyperV: false
    dnsConfiguration: {}
    outboundVnetRouting: {
      allTraffic: false
      applicationTraffic: false
      contentShareTraffic: false
      imagePullTraffic: false
    }
    siteConfig: {
      numberOfWorkers: 1
      acrUseManagedIdentityCreds: false
      alwaysOn: true
      http20Enabled: true
      functionAppScaleLimit: 0
      minimumElasticInstanceCount: 1
    }
    scmSiteAlsoStopped: false
    clientAffinityEnabled: true
    clientAffinityProxyEnabled: false
    clientCertEnabled: false
    clientCertMode: 'Required'
    hostNamesDisabled: false
    ipMode: 'IPv4'
    customDomainVerificationId: 'B180E771A08A40C4E037F2BE78F2B8D306EA32AC67B74E7A036113B4D76088C7'
    containerSize: 0
    dailyMemoryTimeQuota: 0
    httpsOnly: true
    endToEndEncryptionEnabled: false
    redundancyMode: 'None'
    publicNetworkAccess: 'Enabled'
    storageAccountRequired: false
    keyVaultReferenceIdentity: 'SystemAssigned'
  }
}

resource sites_discdb_name_ftp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'ftp'
  location: 'West US'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/59a15238-187b-492e-8301-a40f5731b49a/resourceGroups/thediscdb/providers/microsoft.insights/components/discdb'
  }
  properties: {
    allow: true
  }
}

resource sites_discdb_name_scm 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'scm'
  location: 'West US'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/59a15238-187b-492e-8301-a40f5731b49a/resourceGroups/thediscdb/providers/microsoft.insights/components/discdb'
  }
  properties: {
    allow: true
  }
}

resource sites_discdb_name_web 'Microsoft.Web/sites/config@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'web'
  location: 'West US'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/59a15238-187b-492e-8301-a40f5731b49a/resourceGroups/thediscdb/providers/microsoft.insights/components/discdb'
  }
  properties: {
    numberOfWorkers: 1
    defaultDocuments: [
      'Default.htm'
      'Default.html'
      'Default.asp'
      'index.htm'
      'index.html'
      'iisstart.htm'
      'default.aspx'
      'index.php'
      'hostingstart.html'
    ]
    netFrameworkVersion: 'v9.0'
    requestTracingEnabled: false
    remoteDebuggingEnabled: false
    httpLoggingEnabled: false
    acrUseManagedIdentityCreds: false
    logsDirectorySizeLimit: 35
    detailedErrorLoggingEnabled: false
    publishingUsername: '$discdb'
    scmType: 'GitHubAction'
    use32BitWorkerProcess: false
    webSocketsEnabled: true
    alwaysOn: true
    managedPipelineMode: 'Integrated'
    virtualApplications: [
      {
        virtualPath: '/'
        physicalPath: 'site\\wwwroot'
        preloadEnabled: true
      }
    ]
    loadBalancing: 'LeastRequests'
    experiments: {
      rampUpRules: []
    }
    autoHealEnabled: false
    vnetRouteAllEnabled: false
    vnetPrivatePortsCount: 0
    localMySqlEnabled: false
    ipSecurityRestrictions: [
      {
        ipAddress: 'Any'
        action: 'Allow'
        priority: 2147483647
        name: 'Allow all'
        description: 'Allow all access'
      }
    ]
    scmIpSecurityRestrictions: [
      {
        ipAddress: 'Any'
        action: 'Allow'
        priority: 2147483647
        name: 'Allow all'
        description: 'Allow all access'
      }
    ]
    scmIpSecurityRestrictionsUseMain: false
    http20Enabled: true
    minTlsVersion: '1.2'
    scmMinTlsVersion: '1.0'
    ftpsState: 'AllAllowed'
    preWarmedInstanceCount: 0
    elasticWebAppScaleLimit: 0
    functionsRuntimeScaleMonitoringEnabled: false
    minimumElasticInstanceCount: 1
    azureStorageAccounts: {}
    http20ProxyFlag: 0
  }
}

resource sites_discdb_name_01fd90b1a5684b6b975d6a4f2f8cfc2c 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '01fd90b1a5684b6b975d6a4f2f8cfc2c'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"b6a9069bd3632fd04d6d98790f300a5717bad189","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Fix slug lookup on title details"}'
    start_time: '2024-11-16T22:36:52.8951375Z'
    end_time: '2024-11-16T22:37:07.3678602Z'
    active: false
  }
}

resource sites_discdb_name_0e86f5fbd33040a8b08c6ad2f94e0588 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '0e86f5fbd33040a8b08c6ad2f94e0588'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"a399bfd73395db8580021e2ceda3e6deb7999da1","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Add more routes to boxset title detail to match urls on the site"}'
    start_time: '2024-11-19T21:18:13.879082Z'
    end_time: '2024-11-19T21:18:35.0288906Z'
    active: false
  }
}

resource sites_discdb_name_283ba38dd79d434f90b582c90a1e06b1 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '283ba38dd79d434f90b582c90a1e06b1'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"39274376e37d8950f36d4ef2889a178a1240784e","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Add page title, description, and canonical urls to all pages"}'
    start_time: '2024-11-17T22:19:25.7436528Z'
    end_time: '2024-11-17T22:19:40.5769274Z'
    active: false
  }
}

resource sites_discdb_name_4446692f79a04cd68aec153fd44bc196 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '4446692f79a04cd68aec153fd44bc196'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"066b6c6d17b311296fa151bdfd0ae5c58220c5d7","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"add images that were showing up in 404 logs"}'
    start_time: '2024-11-07T23:57:38.2576889Z'
    end_time: '2024-11-07T23:57:56.488393Z'
    active: false
  }
}

resource sites_discdb_name_4fcf12c52bc643fa981e76cd3175a506 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '4fcf12c52bc643fa981e76cd3175a506'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"70fa50eb2d3910b1f6b3210ea25329b1a77bf692","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Try a workaround for the search box"}'
    start_time: '2025-01-12T17:27:44.7285404Z'
    end_time: '2025-01-12T17:27:59.4703389Z'
    active: false
  }
}

resource sites_discdb_name_6d37ba3b5caf40bd9955a7bc90772dbe 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '6d37ba3b5caf40bd9955a7bc90772dbe'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"7f7a2b56b60860f460be75f32199efc6e802afc5","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":""}'
    start_time: '2025-02-10T14:50:00.7851963Z'
    end_time: '2025-02-10T14:50:22.3976865Z'
    active: true
  }
}

resource sites_discdb_name_8b38d70abe7d49cc9de63baf98e973ca 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '8b38d70abe7d49cc9de63baf98e973ca'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"1fc8a9cc2d2e9ec66adb2645dc7ce0e8b9414077","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Fix SlugOrIndex comparison"}'
    start_time: '2024-11-16T17:12:46.1276472Z'
    end_time: '2024-11-16T17:12:56.4846684Z'
    active: false
  }
}

resource sites_discdb_name_a01f77447f4f40f38a004a815109bcc4 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'a01f77447f4f40f38a004a815109bcc4'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"1d8b3bf942500122ac70cc28f6bcc98e99abbc63","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Upgrade to dotnet 9"}'
    start_time: '2024-11-16T06:09:26.4048035Z'
    end_time: '2024-11-16T06:09:44.8643392Z'
    active: false
  }
}

resource sites_discdb_name_a66dd4c9978b40ce93aaa77c4662f9a0 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'a66dd4c9978b40ce93aaa77c4662f9a0'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"916300b36a6ccac9731ccaa7d722f8e20b91dd08","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Add rss feed"}'
    start_time: '2024-11-08T03:10:19.7125068Z'
    end_time: '2024-11-08T03:10:36.913672Z'
    active: false
  }
}

resource sites_discdb_name_e52b6ebc23b14a3ca09bd4cd2dbefddb 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'e52b6ebc23b14a3ca09bd4cd2dbefddb'
  location: 'West US'
  properties: {
    status: 4
    author_email: 'N/A'
    author: 'N/A'
    deployer: 'GITHUB_ZIP_DEPLOY'
    message: '{"type":"deployment","sha":"f7f39ed3505aa05674322d8ed451ee2adfc7ca53","repoName":"TheDiscDb/web","actor":"lfoust","slotName":"production","commitMessage":"Add aspire back. Also add sitemaps and robots.txt back"}'
    start_time: '2024-11-07T23:27:53.8916851Z'
    end_time: '2024-11-07T23:28:12.1384804Z'
    active: false
  }
}

resource sites_discdb_name_sites_discdb_name_azurewebsites_net 'Microsoft.Web/sites/hostNameBindings@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: '${sites_discdb_name}.azurewebsites.net'
  location: 'West US'
  properties: {
    siteName: 'discdb'
    hostNameType: 'Verified'
  }
}

resource sites_discdb_name_thechapterdb_com 'Microsoft.Web/sites/hostNameBindings@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'thechapterdb.com'
  location: 'West US'
  properties: {
    siteName: 'discdb'
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: '87241FF71753D743C435BFE8B6AAE8ACC1C17361'
  }
}

resource sites_discdb_name_the_sites_discdb_name_com 'Microsoft.Web/sites/hostNameBindings@2024-11-01' = {
  parent: sites_discdb_name_resource
  name: 'the${sites_discdb_name}.com'
  location: 'West US'
  properties: {
    siteName: 'discdb'
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: 'A1EF143192E1854EBAB5F97A1E4B3A98F32AE327'
  }
}

resource servers_thediscdb_name_servers_thediscdb_name_Default 'Microsoft.Sql/servers/databases/advancedThreatProtectionSettings@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'Default'
  properties: {
    state: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource servers_thediscdb_name_servers_thediscdb_name_CreateIndex 'Microsoft.Sql/servers/databases/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'CreateIndex'
  properties: {
    autoExecuteValue: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource servers_thediscdb_name_servers_thediscdb_name_DbParameterization 'Microsoft.Sql/servers/databases/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'DbParameterization'
  properties: {
    autoExecuteValue: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource servers_thediscdb_name_servers_thediscdb_name_DefragmentIndex 'Microsoft.Sql/servers/databases/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'DefragmentIndex'
  properties: {
    autoExecuteValue: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource servers_thediscdb_name_servers_thediscdb_name_DropIndex 'Microsoft.Sql/servers/databases/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'DropIndex'
  properties: {
    autoExecuteValue: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource servers_thediscdb_name_servers_thediscdb_name_ForceLastGoodPlan 'Microsoft.Sql/servers/databases/advisors@2014-04-01' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'ForceLastGoodPlan'
  properties: {
    autoExecuteValue: 'Enabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_auditingPolicies_servers_thediscdb_name_servers_thediscdb_name_Default 'Microsoft.Sql/servers/databases/auditingPolicies@2014-04-01' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'Default'
  location: 'West US'
  properties: {
    auditingState: 'Disabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_auditingSettings_servers_thediscdb_name_servers_thediscdb_name_Default 'Microsoft.Sql/servers/databases/auditingSettings@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'default'
  properties: {
    retentionDays: 0
    isAzureMonitorTargetEnabled: false
    state: 'Disabled'
    storageAccountSubscriptionId: '00000000-0000-0000-0000-000000000000'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_backupLongTermRetentionPolicies_servers_thediscdb_name_servers_thediscdb_name_default 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'default'
  properties: {
    weeklyRetention: 'PT0S'
    monthlyRetention: 'PT0S'
    yearlyRetention: 'PT0S'
    weekOfYear: 0
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_backupShortTermRetentionPolicies_servers_thediscdb_name_servers_thediscdb_name_default 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'default'
  properties: {
    retentionDays: 7
    diffBackupIntervalInHours: 24
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_extendedAuditingSettings_servers_thediscdb_name_servers_thediscdb_name_Default 'Microsoft.Sql/servers/databases/extendedAuditingSettings@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'default'
  properties: {
    retentionDays: 0
    isAzureMonitorTargetEnabled: false
    state: 'Disabled'
    storageAccountSubscriptionId: '00000000-0000-0000-0000-000000000000'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_geoBackupPolicies_servers_thediscdb_name_servers_thediscdb_name_Default 'Microsoft.Sql/servers/databases/geoBackupPolicies@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'Default'
  properties: {
    state: 'Enabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource servers_thediscdb_name_servers_thediscdb_name_Current 'Microsoft.Sql/servers/databases/ledgerDigestUploads@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'Current'
  properties: {}
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_securityAlertPolicies_servers_thediscdb_name_servers_thediscdb_name_Default 'Microsoft.Sql/servers/databases/securityAlertPolicies@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'Default'
  properties: {
    state: 'Disabled'
    disabledAlerts: [
      ''
    ]
    emailAddresses: [
      ''
    ]
    emailAccountAdmins: false
    retentionDays: 0
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_transparentDataEncryption_servers_thediscdb_name_servers_thediscdb_name_Current 'Microsoft.Sql/servers/databases/transparentDataEncryption@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'Current'
  properties: {
    state: 'Enabled'
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource Microsoft_Sql_servers_databases_vulnerabilityAssessments_servers_thediscdb_name_servers_thediscdb_name_Default 'Microsoft.Sql/servers/databases/vulnerabilityAssessments@2024-05-01-preview' = {
  parent: servers_thediscdb_name_servers_thediscdb_name
  name: 'Default'
  properties: {
    recurringScans: {
      isEnabled: false
      emailSubscriptionAdmins: true
    }
  }
  dependsOn: [
    servers_thediscdb_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_azure_webjobs_hosts 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_default
  name: 'azure-webjobs-hosts'
  properties: {
    immutableStorageWithVersioning: {
      enabled: false
    }
    defaultEncryptionScope: '$account-encryption-key'
    denyEncryptionScopeOverride: false
    publicAccess: 'None'
  }
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_searchindexer_name_default_azure_webjobs_hosts 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: storageAccounts_searchindexer_name_default
  name: 'azure-webjobs-hosts'
  properties: {
    immutableStorageWithVersioning: {
      enabled: false
    }
    defaultEncryptionScope: '$account-encryption-key'
    denyEncryptionScopeOverride: false
    publicAccess: 'None'
  }
  dependsOn: [
    storageAccounts_searchindexer_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_azure_webjobs_secrets 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_default
  name: 'azure-webjobs-secrets'
  properties: {
    immutableStorageWithVersioning: {
      enabled: false
    }
    defaultEncryptionScope: '$account-encryption-key'
    denyEncryptionScopeOverride: false
    publicAccess: 'None'
  }
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_configuration 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_default
  name: 'configuration'
  properties: {
    immutableStorageWithVersioning: {
      enabled: false
    }
    defaultEncryptionScope: '$account-encryption-key'
    denyEncryptionScopeOverride: false
    publicAccess: 'None'
  }
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_imagecache 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_default
  name: 'imagecache'
  properties: {
    immutableStorageWithVersioning: {
      enabled: false
    }
    defaultEncryptionScope: '$account-encryption-key'
    denyEncryptionScopeOverride: false
    publicAccess: 'None'
  }
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_images 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: storageAccounts_discdbstatic_name_default
  name: 'images'
  properties: {
    immutableStorageWithVersioning: {
      enabled: false
    }
    defaultEncryptionScope: '$account-encryption-key'
    denyEncryptionScopeOverride: false
    publicAccess: 'Blob'
  }
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_thediscdb_searchindexer_westus2 'Microsoft.Storage/storageAccounts/fileServices/shares@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_fileServices_storageAccounts_discdbstatic_name_default
  name: 'thediscdb-searchindexer-westus2'
  properties: {
    accessTier: 'TransactionOptimized'
    shareQuota: 102400
    enabledProtocols: 'SMB'
  }
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureFunctionsDiagnosticEvents202403 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureFunctionsDiagnosticEvents202403'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureFunctionsDiagnosticEvents202404 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureFunctionsDiagnosticEvents202404'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureFunctionsDiagnosticEvents202405 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureFunctionsDiagnosticEvents202405'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureFunctionsDiagnosticEvents202406 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureFunctionsDiagnosticEvents202406'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureFunctionsDiagnosticEvents202407 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureFunctionsDiagnosticEvents202407'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureFunctionsDiagnosticEvents202408 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureFunctionsDiagnosticEvents202408'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureWebJobsHostLogs202109 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureWebJobsHostLogs202109'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}

resource storageAccounts_discdbstatic_name_default_AzureWebJobsHostLogscommon 'Microsoft.Storage/storageAccounts/tableServices/tables@2025-01-01' = {
  parent: Microsoft_Storage_storageAccounts_tableServices_storageAccounts_discdbstatic_name_default
  name: 'AzureWebJobsHostLogscommon'
  properties: {}
  dependsOn: [
    storageAccounts_discdbstatic_name_resource
  ]
}
