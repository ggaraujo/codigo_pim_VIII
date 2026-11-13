// =============================================================================
// Infraestrutura como código da plataforma Unifica (seção 6.4 do PIM).
// Cria, em um grupo de recursos do Azure: Log Analytics, ACR, AKS, Azure SQL,
// Key Vault, identidade gerenciada da API e orçamento de custos.
//
// Implantação (exemplo):
//   az deployment group create -g rg-unifica-prd -f deploy/infra/main.bicep \
//     -p ambiente=prd adminSqlLogin=<grupo-dba> adminSqlObjectId=<object-id> emailAlertas=<e-mail>
// =============================================================================

@description('Sufixo do ambiente: hml (homologação) ou prd (produção).')
@allowed(['hml', 'prd'])
param ambiente string

param location string = resourceGroup().location

@description('Grupo do Entra ID que administra o Azure SQL (sem login SQL com senha).')
param adminSqlLogin string
param adminSqlObjectId string

@description('E-mail que recebe os alertas de orçamento (FinOps).')
param emailAlertas string

@description('Orçamento mensal do grupo de recursos, em BRL.')
param orcamentoMensal int = ambiente == 'prd' ? 6000 : 1500

param inicioOrcamento string = '2027-01-01'

var nome = 'unifica-${ambiente}'
var producao = ambiente == 'prd'
var tags = {
  sistema: 'unifica'
  ambiente: ambiente
  empresa: 'Alunos Tech'
}

// ---------- Observabilidade --------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${nome}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: producao ? 90 : 30
  }
}

// ---------- Registro de imagens ---------------------------------------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: replace('acr${nome}', '-', '')
  location: location
  tags: tags
  sku: { name: producao ? 'Standard' : 'Basic' }
  properties: {
    adminUserEnabled: false   // acesso só por identidade (AcrPull/AcrPush)
  }
}

// ---------- Cluster Kubernetes ------------------------------------------------
resource aks 'Microsoft.ContainerService/managedClusters@2024-02-01' = {
  name: 'aks-${nome}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    dnsPrefix: 'aks-${nome}'
    agentPoolProfiles: [
      {
        name: 'sistema'
        mode: 'System'
        vmSize: 'Standard_D2s_v5'
        osType: 'Linux'
        count: producao ? 3 : 1
        enableAutoScaling: true
        minCount: producao ? 3 : 1
        maxCount: producao ? 6 : 2
        availabilityZones: producao ? ['1', '2', '3'] : []
      }
    ]
    networkProfile: {
      networkPlugin: 'azure'
      networkPluginMode: 'overlay'
      networkPolicy: 'azure'
    }
    oidcIssuerProfile: { enabled: true }
    securityProfile: {
      workloadIdentity: { enabled: true }
    }
    ingressProfile: {
      webAppRouting: { enabled: true }
    }
    addonProfiles: {
      // Container Insights: métricas e logs dos contêineres no Log Analytics.
      omsagent: {
        enabled: true
        config: { logAnalyticsWorkspaceResourceID: logAnalytics.id }
      }
      azurepolicy: { enabled: true }
    }
    autoUpgradeProfile: { upgradeChannel: 'patch' }
  }
}

// O kubelet do AKS pode baixar imagens do ACR.
var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
resource aksAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, aks.id, acrPullRoleId)
  scope: acr
  properties: {
    principalId: aks.properties.identityProfile.kubeletidentity.objectId
    roleDefinitionId: acrPullRoleId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Identidade da API (Workload Identity) ---------------------------
resource idApi 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${nome}-api'
  location: location
  tags: tags
}

resource federacao 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: idApi
  name: 'aks-unifica-api'
  properties: {
    issuer: aks.properties.oidcIssuerProfile.issuerURL
    subject: 'system:serviceaccount:unifica-${ambiente == 'prd' ? 'producao' : 'homologacao'}:unifica-api'
    audiences: ['api://AzureADTokenExchange']
  }
}

// ---------- Banco de dados ----------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql-${nome}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: adminSqlLogin
      sid: adminSqlObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true   // desativa logins SQL com senha
    }
  }
}

// Permite conexões de serviços do Azure (AKS). Evolução prevista: endpoint privado.
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: 'Unifica'
  location: location
  tags: tags
  sku: producao
    ? { name: 'GP_Gen5_2', tier: 'GeneralPurpose' }
    : { name: 'GP_S_Gen5_1', tier: 'GeneralPurpose' }   // homologação: serverless, pausa sozinho
  properties: {
    zoneRedundant: producao
    autoPauseDelay: producao ? -1 : 60
    requestedBackupStorageRedundancy: producao ? 'Geo' : 'Local'
  }
}

// Logs do banco no Log Analytics, incluindo deadlocks e bloqueios (seção 5.5).
resource sqlDiagnostico 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'diag-unifica'
  scope: sqlDb
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      { category: 'SQLInsights', enabled: true }
      { category: 'QueryStoreRuntimeStatistics', enabled: true }
      { category: 'Errors', enabled: true }
      { category: 'Deadlocks', enabled: true }
      { category: 'Blocks', enabled: true }
      { category: 'Timeouts', enabled: true }
    ]
    metrics: [
      { category: 'Basic', enabled: true }
    ]
  }
}

// ---------- Segredos ----------------------------------------------------------
resource cofre 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${nome}-${take(uniqueString(resourceGroup().id), 5)}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

// ---------- FinOps: orçamento com alertas -----------------------------------
resource orcamento 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'orcamento-${nome}'
  properties: {
    category: 'Cost'
    amount: orcamentoMensal
    timeGrain: 'Monthly'
    timePeriod: { startDate: inicioOrcamento }
    notifications: {
      atingiu80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        contactEmails: [emailAlertas]
      }
      previsao100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [emailAlertas]
      }
    }
  }
}

output acrLoginServer string = acr.properties.loginServer
output aksNome string = aks.name
output sqlServidor string = sqlServer.properties.fullyQualifiedDomainName
output apiClientId string = idApi.properties.clientId
