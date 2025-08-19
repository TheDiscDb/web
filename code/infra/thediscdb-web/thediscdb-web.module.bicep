@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param prod_outputs_planid string

param thediscdb_web_containerport string

param sql_outputs_sqlserverfqdn string

param storage_outputs_blobendpoint string

param thediscdb_web_identity_outputs_clientid string

param sites_discdb_name string = 'discdb'

resource webapp 'Microsoft.Web/sites@2024-11-01' = {
  name: sites_discdb_name
  location: location
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
    serverFarmId: prod_outputs_planid
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
      appSettings: [
        {
          name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EXCEPTION_LOG_ATTRIBUTES'
          value: 'true'
        }
        {
          name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EVENT_LOG_ATTRIBUTES'
          value: 'true'
        }
        {
          name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY'
          value: 'in_memory'
        }
        {
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
        {
          name: 'HTTP_PORTS'
          value: thediscdb_web_containerport
        }
        {
          name: 'ConnectionStrings__thediscdb'
          value: 'Server=tcp:${sql_outputs_sqlserverfqdn},1433;Encrypt=True;Authentication="Active Directory Default";Database=thediscdb'
        }
        {
          name: 'ConnectionStrings__blobs'
          value: storage_outputs_blobendpoint
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: thediscdb_web_identity_outputs_clientid
        }
      ]
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
