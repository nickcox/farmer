namespace Farmer

open Farmer.Internal

module Helpers =
    module AppInsights =
        let instrumentationKey (ResourceName accountName) =
            sprintf "[reference(concat('Microsoft.Insights/components/', '%s')).InstrumentationKey]" accountName
    module Locations =
        let ``East Asia`` = "eastasia"
        let ``Southeast Asia`` = "southeastasia"
        let ``Central US`` = "centralus"
        let ``East US`` = "eastus"
        let ``East US 2`` = "eastus2"
        let ``West US`` = "westus"
        let ``North Central US`` = "northcentralus"
        let ``South Central US`` = "southcentralus"
        let ``North Europe`` = "northeurope"
        let ``West Europe`` = "westeurope"
        let ``Japan West`` = "japanwest"
        let ``Japan East`` = "japaneast"
        let ``Brazil South`` = "brazilsouth"
        let ``Australia East`` = "australiaeast"
        let ``Australia Southeast`` = "australiasoutheast"
        let ``South India`` = "southindia"
        let ``Central India`` = "centralindia"
        let ``West India`` = "westindia"

[<AutoOpen>]
module WebApp =
    module Sku =
        let F1 = "F1"
        let B1 = "B1"
        let B2 = "B2"
        let B3 = "B3"
        let S1 = "S1"
        let S2 = "S2"
        let S3 = "S3"
        let P1 = "P1"
        let P2 = "P2"
        let P3 = "P3"
        let P1V2 = "P1V2"
        let P2V2 = "P2V2"
        let P3V2 = "P3V2"
        let I1 = "I1"
        let I2 = "I2"
        let I3 = "I3"

    module AppSettings =
        let WebsiteNodeDefaultVersion version = "WEBSITE_NODE_DEFAULT_VERSION", version
        let RunFromPackage = "WEBSITE_RUN_FROM_PACKAGE", "1"
    type WebAppConfig =
        { Name : ResourceName
          ServicePlanName : ResourceName
          Sku : string
          AppInsightsName : ResourceName option
          RunFromPackage : bool
          WebsiteNodeDefaultVersion : string option
          Settings : Map<string, string>
          Dependencies : ResourceName list }
        member this.PublishingPassword =
            sprintf "[list(resourceId('Microsoft.Web/sites/config', '%s', 'publishingcredentials'), '2014-06-01').properties.publishingPassword]" this.Name.Value
        
    type WebAppBuilder() =
        member __.Yield _ =
            { Name = ResourceName ""
              ServicePlanName = ResourceName ""
              Sku = Sku.F1
              AppInsightsName = None
              RunFromPackage = false
              WebsiteNodeDefaultVersion = None
              Settings = Map.empty
              Dependencies = [] }
        member __.Run (state:WebAppConfig) =
            { state with
                Dependencies = state.ServicePlanName :: state.Dependencies }
        /// Sets the name of the web app; use the `name` keyword.
        [<CustomOperation "name">]
        member __.Name(state:WebAppConfig, name) = { state with Name = name }
        member this.Name(state:WebAppConfig, name:string) = this.Name(state, ResourceName name)
        /// Sets the name of service plan of the web app; use the `service_plan_name` keyword.
        [<CustomOperation "service_plan_name">]
        member __.ServicePlanName(state:WebAppConfig, name) = { state with ServicePlanName = name }
        member this.ServicePlanName(state:WebAppConfig, name:string) = this.ServicePlanName(state, ResourceName name)
        /// Sets the sku of the web app; use the `sku` keyword.
        [<CustomOperation "sku">]
        member __.Sku(state:WebAppConfig, sku:string) = { state with Sku = sku }
        /// Creates a fully-configured application insights resource linked to this web app; use the `use_app_insights` keyword.
        [<CustomOperation "use_app_insights">]
        member __.UseAppInsights(state:WebAppConfig, name) = { state with AppInsightsName = Some name }
        member this.UseAppInsights(state:WebAppConfig, name:string) = this.UseAppInsights(state, ResourceName name)
        /// Sets the web app to use run from package mode; use the `run_from_package` keyword.
        [<CustomOperation "run_from_package">]
        member __.RunFromPackage(state:WebAppConfig) = { state with RunFromPackage = true }
        /// Sets the node version of the web app; use the `website_node_default_version` keyword.
        [<CustomOperation "website_node_default_version">]
        member __.NodeVersion(state:WebAppConfig, version) = { state with WebsiteNodeDefaultVersion = Some version }
        /// Sets an app setting of the web app; use the `setting` keyword.
        [<CustomOperation "setting">]
        member __.AddSetting(state:WebAppConfig, key, value) = { state with Settings = state.Settings.Add(key, value) }
        /// Sets a dependency for the web app; use the `depends_on` keyword.
        [<CustomOperation "depends_on">]
        member __.DependsOn(state:WebAppConfig, resourceName) =
            { state with Dependencies = resourceName :: state.Dependencies }
    let webApp = WebAppBuilder()

[<AutoOpen>]
module Storage =
    module Sku =
        let StandardLRS = "Standard_LRS"
        let StandardGRS = "Standard_GRS"
        let StandardRAGRS = "Standard_RAGRS"
        let StandardZRS = "Standard_ZRS"
        let StandardGZRS = "Standard_GZRS"
        let StandardRAGZRS = "Standard_RAGZRS"
        let PremiumLRS = "Premium_LRS"
        let PremiumZRS = "Premium_ZRS"
    type StorageAccountConfig =
        { /// The name of the storage account.
          Name : ResourceName
          /// The sku of the storage account.
          Sku : string }
        member this.Key =
            let (ResourceName name) = this.Name
            sprintf
                "[concat('DefaultEndpointsProtocol=https;AccountName=', '%s', ';AccountKey=', listKeys(resourceId('Microsoft.Storage/storageAccounts/', '%s'), '2017-10-01').keys[0].value)]"
                    name
                    name
    type StorageAccountBuilder() =
        member __.Yield _ = { Name = ResourceName ""; Sku = Sku.StandardLRS }
        [<CustomOperation "name">]
        member __.Name(state:StorageAccountConfig, name) = { state with Name = name }
        member this.Name(state:StorageAccountConfig, name) = this.Name(state, ResourceName name)
        [<CustomOperation "sku">]
        member __.Sku(state:StorageAccountConfig, sku) = { state with Sku = sku }
    let storageAccount = StorageAccountBuilder()

    open WebApp
    type WebAppBuilder with
        member this.DependsOn(state:WebAppConfig, storageAccountConfig:StorageAccountConfig) =
            this.DependsOn(state, storageAccountConfig.Name)

[<AutoOpen>]
module CosmosDb =
    type CosmosDbContainerConfig =
        { Name : ResourceName
          PartitionKey : string list * CosmosDbIndexKind
          Indexes : (string * (CosmosDbIndexDataType * CosmosDbIndexKind) list) list
          ExcludedPaths : string list }

    type CosmosDbConfig =
        { ServerName : ResourceName
          ServerConsistencyPolicy : ConsistencyPolicy
          ServerFailoverPolicy : FailoverPolicy
          DbName : ResourceName          
          DbThroughput : string
          Containers : CosmosDbContainerConfig list }    

    type CosmosDbContainer() =
        member __.Yield _ =
            { Name = ResourceName ""
              PartitionKey = [], Hash
              Indexes = []
              ExcludedPaths = [] }

        [<CustomOperation "name">]
        member __.Name (state:CosmosDbContainerConfig, name) =
            { state with Name = ResourceName name }

        [<CustomOperation "partition_key">]
        member __.PartitionKey (state:CosmosDbContainerConfig, partitions, indexKind) =
            { state with PartitionKey = partitions, indexKind }

        [<CustomOperation "include_index">]
        member __.IncludeIndex (state:CosmosDbContainerConfig, path, indexes) =
            { state with Indexes = (path, indexes) :: state.Indexes }

        [<CustomOperation "exclude_path">]
        member __.ExcludePath (state:CosmosDbContainerConfig, path) =
            { state with ExcludedPaths = path :: state.ExcludedPaths }

    type CosmosDbBuilder() =
        member __.Yield _ =
            { DbName = ResourceName "CosmosDatabase"
              ServerName = ResourceName "CosmosServer"            
              ServerConsistencyPolicy = Eventual
              ServerFailoverPolicy = NoFailover
              DbThroughput = "400"
              Containers = [] }
        /// Sets the name of cosmos db server; use the `server_name` keyword.
        [<CustomOperation "server_name">]
        member __.ServerName(state:CosmosDbConfig, serverName) = { state with ServerName = serverName }
        member this.ServerName(state:CosmosDbConfig, serverName:string) = this.ServerName(state, ResourceName serverName)
        /// Sets the name of the web app; use the `name` keyword.
        [<CustomOperation "name">]
        member __.Name(state:CosmosDbConfig, name) = { state with DbName = name }
        member this.Name(state:CosmosDbConfig, name:string) = this.Name(state, ResourceName name)
        /// Sets the sku of the web app; use the `sku` keyword.
        [<CustomOperation "consistency_policy">]
        member __.ConsistencyPolicy(state:CosmosDbConfig, consistency:ConsistencyPolicy) = { state with ServerConsistencyPolicy = consistency }
        [<CustomOperation "failover_policy">]
        member __.FailoverPolicy(state:CosmosDbConfig, failoverPolicy:FailoverPolicy) = { state with ServerFailoverPolicy = failoverPolicy }
        [<CustomOperation "throughput">]
        member __.Throughput(state:CosmosDbConfig, throughput) = { state with DbThroughput = throughput }
        member this.Throughput(state:CosmosDbConfig, throughput:int) = this.Throughput(state, string throughput)
        [<CustomOperation "add_containers">]
        member __.AddContainers(state:CosmosDbConfig, containers) =
            { state with Containers = state.Containers @ containers }

    open WebApp
    type WebAppBuilder with
        member this.DependsOn(state:WebAppConfig, cosmosDbConfig:CosmosDbConfig) =
            this.DependsOn(state, cosmosDbConfig.DbName)

    let cosmosDb = CosmosDbBuilder()
    let container = CosmosDbContainer()

[<AutoOpen>]
module SqlAzure =
    type Edition = Free | Basic | Standard of string | Premium of string
    module Sku =
        let ``Free`` = Free
        let ``Basic`` = Basic
        let ``S0`` = Standard "S0"
        let ``S1`` = Standard "S1"
        let ``S2`` = Standard "S2"
        let ``S3`` = Standard "S3"
        let ``S4`` = Standard "S4"
        let ``S6`` = Standard "S6"
        let ``S7`` = Standard "S7"
        let ``S9`` = Standard "S9"
        let ``S12`` =Standard "S12"
        let ``P1`` = Premium "P1"
        let ``P2`` = Premium "P2"
        let ``P4`` = Premium "P4"
        let ``P6`` = Premium "P6"
        let ``P11`` = Premium "P11"
        let ``P15`` = Premium "P15"
    type SqlAzureConfig =
        { ServerName : ResourceName
          AdministratorCredentials : {| UserName : string; Password : SecureParameter |}
          DbName : ResourceName
          DbEdition : Edition
          DbCollation : string
          Encryption : bool
          FirewallRules : {| Start : System.Net.IPAddress; End : System.Net.IPAddress |} list }
        member this.FullyQualifiedDomainName =
            sprintf "[reference(concat('Microsoft.Sql/servers/', variables('%s'))).fullyQualifiedDomainName]" this.ServerName.Value
    type SqlBuilder() =
        member __.Yield _ =
            { ServerName = ResourceName ""
              AdministratorCredentials = {| UserName = ""; Password = SecureParameter "sql_password" |}
              DbName = ResourceName ""
              DbEdition = Free
              DbCollation = "SQL_Latin1_General_CP1_CI_AS"
              Encryption = false
              FirewallRules = [] }

        member __.Run(state) =
            { state with
                AdministratorCredentials =
                    {| state.AdministratorCredentials with
                        Password = SecureParameter (sprintf "password-for-%s" state.ServerName.Value) |} }
        [<CustomOperation "server_name">]
        member __.ServerName(state:SqlAzureConfig, serverName) = { state with ServerName = serverName }
        member this.ServerName(state:SqlAzureConfig, serverName:string) = this.ServerName(state, ResourceName serverName)
        [<CustomOperation "db_name">]
        member __.Name(state:SqlAzureConfig, name) = { state with DbName = name }
        member this.Name(state:SqlAzureConfig, name:string) = this.Name(state, ResourceName name)
        [<CustomOperation "db_edition">]
        member __.DatabaseEdition(state:SqlAzureConfig, edition:Edition) = { state with DbEdition = edition }
        [<CustomOperation "collation">]
        member __.Collation(state:SqlAzureConfig, collation:string) = { state with DbCollation = collation }
        [<CustomOperation "use_encryption">]
        member __.Encryption(state:SqlAzureConfig) = { state with Encryption = true }
        [<CustomOperation "firewall_rule">]
        member __.AddFirewallWall(state:SqlAzureConfig, startRange, endRange) =
            { state with
                FirewallRules =
                    {| Start = System.Net.IPAddress.Parse startRange; End = System.Net.IPAddress.Parse endRange |}
                    :: state.FirewallRules }
        [<CustomOperation "use_azure_firewall">]
        member this.UseAzureFirewall(state:SqlAzureConfig) =
            this.AddFirewallWall(state, "0.0.0.0", "0.0.0.0")
        [<CustomOperation "admin_username">]
        member __.AdminUsername(state:SqlAzureConfig, username) =
            { state with
                AdministratorCredentials =
                    {| state.AdministratorCredentials with
                        UserName = username |} }
    open WebApp
    type WebAppBuilder with
        member this.DependsOn(state:WebAppConfig, sqlDb:SqlAzureConfig) =
            this.DependsOn(state, sqlDb.ServerName)

    let sql = SqlBuilder()
type ArmConfig =
    { Parameters : string Set
      Variables : (string * string) list
      Outputs : (string * string) list
      Location : string
      Resources : obj list }

[<AutoOpen>]
module ArmBuilder =
    type ArmBuilder() =
        member __.Yield _ =
            { Parameters = Set.empty
              Variables = List.empty
              Outputs = List.empty
              Resources = List.empty
              Location = Helpers.Locations.``West Europe`` }

        member __.Run (state:ArmConfig) = {
            Parameters = state.Parameters |> Set.toList
            Outputs = state.Outputs
            Variables = state.Variables
            Resources =
                state.Resources
                |> List.collect(function
                | :? StorageAccountConfig as sac ->
                    [ { Location = state.Location; Name = sac.Name; Sku = sac.Sku } ]
                | :? WebAppConfig as wac -> [
                    let webApp =
                        { Name = wac.Name
                          AppSettings = [
                            yield! Map.toList wac.Settings
                            if wac.RunFromPackage then yield WebApp.AppSettings.RunFromPackage

                            match wac.WebsiteNodeDefaultVersion with
                            | Some v -> yield WebApp.AppSettings.WebsiteNodeDefaultVersion v
                            | None -> ()

                            match wac.AppInsightsName with
                            | Some v ->
                                yield "APPINSIGHTS_INSTRUMENTATIONKEY", Helpers.AppInsights.instrumentationKey v
                                yield "APPINSIGHTS_PROFILERFEATURE_VERSION", "1.0.0"
                                yield "APPINSIGHTS_SNAPSHOTFEATURE_VERSION", "1.0.0"
                                yield "ApplicationInsightsAgent_EXTENSION_VERSION", "~2"
                                yield "DiagnosticServices_EXTENSION_VERSION", "~3"
                                yield "InstrumentationEngine_EXTENSION_VERSION", "~1"
                                yield "SnapshotDebugger_EXTENSION_VERSION", "~1"
                                yield "XDT_MicrosoftApplicationInsights_BaseExtensions", "~1"
                                yield "XDT_MicrosoftApplicationInsights_Mode", "recommended"
                            | None ->
                                ()
                          ]

                          Extensions =
                            match wac.AppInsightsName with
                            | Some _ -> Set [ AppInsightsExtension ]
                            | None -> Set.empty

                          Dependencies = [
                            yield! wac.Dependencies
                            match wac.AppInsightsName with
                            | Some appInsightsame -> yield appInsightsame
                            | None -> ()
                          ]
                        }

                    let serverFarm =
                        { Location = state.Location
                          Name = wac.ServicePlanName
                          Sku = wac.Sku
                          WebApps = [ webApp ] }

                    yield serverFarm
                    match wac.AppInsightsName with
                    | Some ai ->
                        yield { Name = ai; Location = state.Location; LinkedWebsite = wac.Name }
                    | None ->
                        () ]
                | :? CosmosDbConfig as cosmos -> [
                    { Name = cosmos.ServerName
                      Location = state.Location
                      ConsistencyPolicy = cosmos.ServerConsistencyPolicy
                      WriteModel = cosmos.ServerFailoverPolicy
                      Databases =
                        [ { Name = cosmos.DbName
                            Dependencies = [ cosmos.ServerName ]
                            Throughput = cosmos.DbThroughput
                            Containers =
                                cosmos.Containers
                                |> List.map(fun c ->
                                    { CosmosDbContainer.Name = c.Name
                                      PartitionKey =
                                        {| Paths = fst c.PartitionKey
                                           Kind = snd c.PartitionKey |}
                                      IndexingPolicy =
                                        {| ExcludedPaths = c.ExcludedPaths
                                           IncludedPaths =
                                               c.Indexes
                                               |> List.map(fun index ->
                                                 {| Path = fst index
                                                    Indexes =
                                                        index
                                                        |> snd
                                                        |> List.map(fun (dataType, kind) ->
                                                            {| DataType = dataType
                                                               Kind = kind |})
                                                 |})
                                        |}
                                    })
                          }
                        ]
                    } ]
                | :? SqlAzureConfig as sql -> [
                    { ServerName = sql.ServerName
                      Location = state.Location
                      AdministratorLogin = sql.AdministratorCredentials.UserName
                      AdministratorLoginPassword = sql.AdministratorCredentials.Password
                      DbName = sql.DbName
                      DbEdition =
                        match sql.DbEdition with
                        | Basic -> "Basic"
                        | Free -> "Free"
                        | Standard _ -> "Standard"
                        | Premium _ -> "Premium"
                      DbObjective =
                        match sql.DbEdition with
                        | Basic -> "Basic"
                        | Free -> "Free"
                        | Standard s -> s
                        | Premium p -> p
                      DbCollation = sql.DbCollation
                      TransparentDataEncryption = sql.Encryption
                      FirewallRules = sql.FirewallRules
                    } ]
                | r ->
                    failwithf "Sorry, I don't know how to handle this resource of type '%s'." (r.GetType().FullName))
        }

        /// Creates a variable; use the `variable` keyword.
        [<CustomOperation "variable">]
        member __.AddVariable (state, key, value) : ArmConfig =
            { state with
                Variables = (key, value) :: state.Variables }

        /// Creates a parameter; use the `parameter` keyword.
        [<CustomOperation "parameter">]
        member __.AddParameter (state, parameter) : ArmConfig =
            { state with
                Parameters = state.Parameters.Add parameter }

        /// Creates a list of parameters; use the `parameters` keyword.
        [<CustomOperation "parameters">]
        member __.AddParameters (state, parameters) : ArmConfig =
            { state with
                Parameters = state.Parameters + (Set.ofList parameters) }

        /// Creates an output; use the `output` keyword.
        [<CustomOperation "output">]
        member __.Output (state, outputName, outputValue) : ArmConfig = { state with Outputs = (outputName, outputValue) :: state.Outputs }
        member this.Output (state:ArmConfig, outputName:string, (ResourceName outputValue)) = this.Output(state, outputName, outputValue)

        /// Sets the default location of all resources; use the `location` keyword.
        [<CustomOperation "location">]
        member __.Location (state, location) : ArmConfig = { state with Location = location }

        /// Adds a resource to the template; use the `resource` keyword.
        [<CustomOperation "resource">]
        member __.AddResource(state, resource) : ArmConfig =
            { state with Resources = box resource :: state.Resources }
    let arm = ArmBuilder()