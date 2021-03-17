module ContainerGroup

open Expecto
open Farmer
open Farmer.Identity
open Farmer.ContainerGroup
open Farmer.Arm.ContainerInstance
open Farmer.Builders
open Microsoft.Azure.Management.ContainerInstance
open Microsoft.Azure.Management.ContainerInstance.Models
open Microsoft.Rest
open System

let nginx = containerInstance {
    name "nginx"
    image "nginx:1.17.6-alpine"
    add_ports PublicPort [ 80us; 443us ]
    add_ports InternalPort [ 9090us; ]
    memory 0.5<Gb>
    cpu_cores 1
}
let fsharpApp = containerInstance {
    name "fsharpApp"
    image "myapp:1.7.2"
    add_ports PublicPort [ 8080us ]
    memory 1.5<Gb>
    cpu_cores 2
}
let appWithoutPorts = containerInstance {
    name "appWithoutPorts"
    image "myapp:1.7.2"
    memory 1.5<Gb>
    cpu_cores 2
}

/// Client instance needed to get the serializer settings.
let dummyClient = new ContainerInstanceManagementClient (Uri "http://management.azure.com", TokenCredentials "NotNullOrWhiteSpace")

let asAzureResource (group:ContainerGroupConfig) =
    arm { add_resource group }
    |> findAzureResources<ContainerGroup> dummyClient.SerializationSettings
    |> List.head
    |> fun r ->
        r.Validate()
        r

let tests = testList "Container Group" [
    test "Single container in a group is correctly created" {
        let group =
            containerGroup {
                name "appWithHttpFrontend"
                operating_system Linux
                restart_policy AlwaysRestart
                add_udp_port 123us
                add_instances [ nginx ]
                network_profile "test"
            } |> asAzureResource

        Expect.equal group.Name "appWithHttpFrontend" "Group name is not set correctly."
        Expect.equal group.OsType "Linux" "OS should be Linux"
        Expect.equal group.IpAddress.Ports.[1].PortProperty 123 "Incorrect udp port"
        Expect.equal group.NetworkProfile.Id "[resourceId('Microsoft.Network/networkProfiles', 'test')]" "Incorrect network profile reference"

        let containerInstance = group.Containers.[0]
        Expect.equal containerInstance.Image "nginx:1.17.6-alpine" "Incorrect image"
        Expect.equal containerInstance.Name "nginx" "Incorrect instance name"
        Expect.equal containerInstance.Resources.Requests.MemoryInGB 0.5 "Incorrect memory"
        Expect.equal containerInstance.Resources.Requests.Cpu 1.0 "Incorrect CPU"
        let ports = containerInstance.Ports |> Seq.map(fun p -> p.Port) |> Seq.toList
        Expect.equal ports [ 80; 443; 9090 ] "Incorrect ports on container"
    }

    test "Container group with init containers" {
        let group =
            let emptyDir1 = "emptyDir1"
            containerGroup {
                name "appWithInitContainers"
                add_volumes [
                    volume_mount.empty_dir emptyDir1
                ]
                add_init_containers [
                    initContainer {
                        name "init"
                        image "busybox"
                        command_line [
                            "/bin/sh"
                            "-c"
                            "sleep 60; echo python wordcount.py http://shakespeare.mit.edu/romeo_juliet/full.html > /mnt/emptydir/command_line.txt"
                        ]
                        add_volume_mount emptyDir1 "/mnt/emptydir"
                    }
                ]
                add_instances [
                    containerInstance {
                        name "hamlet"
                        image "mcr.microsoft.com/azuredocs/aci-wordcount"
                        add_volume_mount emptyDir1 "/mnt/emptydir"
                        env_vars [
                            "NumWords", "3"
                            "MinLength", "5"
                        ]
                    }
                ]
            } |> asAzureResource

        let containerInstance = group.Containers.[0]
        Expect.equal containerInstance.Image "mcr.microsoft.com/azuredocs/aci-wordcount" "Incorrect containerInstance image"
        Expect.equal containerInstance.Name "hamlet" "Incorrect containerInstance name"
        let initContainer = group.InitContainers.[0]
        Expect.equal initContainer.Image "busybox" "Incorrect initContainer image"
        Expect.equal initContainer.Name "init" "Incorrect initContainer name"
    }

    test "Group without public ip" {
        let group = containerGroup {
            name "myGroup"
            operating_system Linux
            restart_policy RestartOnFailure
            add_instances [ appWithoutPorts ]
        }

        Expect.isNone group.IpAddress "IpAddresses should be none"
    }

    test "Container with command line arguments" {
        let containerInstance = containerInstance {
            name "appWithCommand"
            image "myapp:1.7.2"
            memory 1.5<Gb>
            cpu_cores 2
            command_line [ "echo"; "hello world" ]
        }

        Expect.equal containerInstance.Command [ "echo"; "hello world" ] "Incorrect container command line arguments"
    }

    test "Multiple containers are correctly added" {
        let group = containerGroup { add_instances [ nginx; fsharpApp ] } |> asAzureResource

        Expect.hasLength group.Containers 2 "Should be two containers"
        Expect.equal group.Containers.[0].Name "nginx" "Incorrect container name"
        Expect.equal group.Containers.[1].Name "fsharpapp" "Incorrect container name"
        Expect.equal group.Containers.[1].Resources.Requests.MemoryInGB 1.5 "Incorrect memory"
        Expect.equal group.Containers.[1].Resources.Requests.Cpu 2.0 "Incorrect CPU count"
    }

    test "Implicitly creates public ports for group based on instances" {
        let group = containerGroup { add_instances [ nginx ] } |> asAzureResource

        let ports = group.IpAddress.Ports |> Seq.map(fun p -> p.PortProperty) |> Seq.toList
        Expect.equal ports ([ 80; 443 ]) "Incorrect implicitly created public ports"
        Expect.hasLength group.Containers.[0].Ports 3 "Incorrect number of private port"
    }

    test "Does not create two ports with the same number across public and private" {
        let group =
            containerGroup {
                add_instances [
                    containerInstance {
                        name "foo"
                        add_ports PublicPort [ 123us ]
                        add_ports InternalPort [ 123us ]
                    }
                ]
            } |> asAzureResource

        Expect.equal group.IpAddress null "Should not be any public ports"
        Expect.equal group.Containers.[0].Ports.[0].Port 123 "Incorrect private port"
        Expect.hasLength group.Containers.[0].Ports 1 "Should only be one port"
    }

    test "Adds container group with volumes mounted on each container." {
        let helloShared1 = containerInstance {
            name "hello-shared-dir1"
            image "mcr.microsoft.com/azuredocs/aci-helloworld:latest"
            add_ports PublicPort [ 80us ]
            add_volume_mount "shared-socket" "/var/lib/shared/hello"
            add_volume_mount "source-code" "/src/farmer"
            add_volume_mount "secret-files" "/config/secrets"
        }
        let helloShared2 = containerInstance {
            name "hello-shared-dir2"
            add_ports PublicPort [ 81us ]
            env_vars [ "testing", "environment variables" ]
            image "mcr.microsoft.com/azuredocs/aci-helloworld:latest"
            add_volume_mount "shared-socket" "/var/lib/shared/hello"
            add_volume_mount "azure-file" "/var/lib/files"
        }
        let group =
            containerGroup {
                name "containersWithFiles"
                add_instances [ helloShared1; helloShared2 ]
                add_volumes [
                    volume_mount.azureFile "azure-file" "fileShare1" "storageaccount1"
                    volume_mount.secret_string "secret-files" "secret1" "abcdefg"
                    volume_mount.empty_dir "shared-socket"
                    volume_mount.git_repo "source-code" (Uri "https://github.com/CompositionalIT/farmer")
                ]
            } |> asAzureResource

        Expect.equal group.Name "containersWithFiles" "Incorrect name on container group"
        Expect.equal group.Containers.[0].VolumeMounts.Count 3 "Incorrect number of volume mounts on container 1"
        Expect.equal group.Containers.[1].VolumeMounts.Count 2 "Incorrect number of volume mounts on container 1"
        Expect.hasLength group.Volumes 4 "Incorrect number of volumes in group"
        Expect.isNotNull group.Volumes.[0].AzureFile "Azure file volume should not be null"
        Expect.isNotNull group.Volumes.[1].Secret "Secret volume should not be null"
        Expect.isNotNull group.Volumes.[2].EmptyDir "Empty directory volume should not be null"
        Expect.isNotNull group.Volumes.[3].GitRepo "Git repo volume should not be null"
    }

    test "Container group with private registry" {
        let group =
            containerGroup {
                add_instances [ nginx ]
                add_registry_credentials [
                    registry "my-registry.azurecr.io" "user"
                ]
            } |> asAzureResource
        Expect.hasLength group.ImageRegistryCredentials 1 "Expected one image registry credential"
        let credentials = group.ImageRegistryCredentials.[0]
        Expect.equal credentials.Server "my-registry.azurecr.io" "Incorrect container image registry server"
        Expect.equal credentials.Username "user" "Incorrect container image registry user"
        Expect.equal credentials.Password "[parameters('my-registry.azurecr.io-password')]" "Container image registry password should be secure parameter"
    }

    test "Container group with system assigned identity" {
        let group =
            containerGroup {
                name "myapp"
                add_instances [ nginx ]
                system_identity
            } |> asAzureResource

        Expect.isTrue group.Identity.Type.HasValue "Expecting an assigned identity."
        Expect.equal group.Identity.Type.Value ResourceIdentityType.SystemAssigned "Expecting a system assigned identity"
    }

    test "Container group with user assigned identity" {
        let group =
            containerGroup {
                name "myapp"
                add_instances [ nginx ]
                add_identity (ResourceId.create(Arm.ManagedIdentity.userAssignedIdentities, ResourceName "user", "resourceGroup") |> UserAssignedIdentity)
            } |> asAzureResource

        Expect.hasLength group.Identity.UserAssignedIdentities 1 "No user assigned identity."
    }

    test "Make container group with MSI" {
        let msi = createUserAssignedIdentity "aciUser"
        let group =
            containerGroup {
                name "myapp-with-msi"
                add_instances [ nginx ]
                add_identity msi
            }
        let template = arm {
            location Location.EastUS
            add_resource msi
            add_resource group
        }
        let containerGroup = template.Template.Resources |> List.find(fun r -> r.ResourceId.Name.Value = "myapp-with-msi") :?> Farmer.Arm.ContainerInstance.ContainerGroup
        Expect.isNonEmpty containerGroup.Identity.UserAssigned "Container group did not have identity"
        Expect.equal containerGroup.Identity.UserAssigned.[0] (UserAssignedIdentity(ResourceId.create(Arm.ManagedIdentity.userAssignedIdentities, ResourceName "aciUser"))) "Expected user identity named 'aciUser'."
    }
    test "Secure environment variables are generated correctly" {
        let cg = containerGroup {
            name "myapp"
            add_instances [
                containerInstance {
                    name "nginx"
                    image "nginx:1.17.6-alpine"
                    env_vars [
                        EnvVar.createSecure "foo" "secret-foo"
                    ]
                }
            ]
        }
        let deployment = arm {
            add_resource cg
        }
        Expect.hasLength deployment.Template.Parameters 1 "Should have a secure parameter for environment variable"
        Expect.equal (deployment.Template.Parameters.Head.ArmExpression.Eval()) "[parameters('secret-foo')]" "Generated incorrect secure parameter."
    }
    test "Secure parameters for secret volume is generated correctly" {
        let cg = containerGroup {
            name "myapp"
            add_instances [
                containerInstance {
                    name "nginx"
                    image "nginx:1.17.6-alpine"
                    add_volume_mount "secrets" "/config/secrets"
                }
            ]
            add_volumes [
                volume_mount.secret_parameter "secrets" "foo" "secret-foo"
            ]
        }
        let deployment = arm {
            location Location.EastUS
            add_resource cg
        }
        Expect.hasLength deployment.Template.Parameters 1 "Should have a secure parameter for secret volume"
        Expect.equal (deployment.Template.Parameters.Head.ArmExpression.Eval()) "[parameters('secret-foo')]" "Generated incorrect secure parameter."
    }
    test "Container with liveliness and readiness probes" {

        let cg =
            containerGroup {
                name "myapp"
                add_instances [
                    containerInstance {
                        name "nginx"
                        image "nginx:1.17.6-alpine"
                        probes [
                            liveliness {
                                http "https://whatever.com:8080/healthcheck"
                                period_seconds 30 // Wait 30 seconds between each liveliness check
                                failure_threshold 10 // After 10 tries, consider this unhealthy
                            }
                            readiness {
                                http "https://whatever.com:8080/healthcheck"
                                initial_delay_seconds 30 // Wait 30 seconds after the container is started before a readiness check
                                failure_threshold 5 // Let it retry 5 times, giving another 50 seconds to try to start
                            }
                        ]
                    }
                ]
            } |> asAzureResource
        let livelinessProbe = cg.Containers.[0].LivenessProbe
        Expect.isNotNull livelinessProbe "Resulting container should have a liveliness probe"
        Expect.equal livelinessProbe.HttpGet.Path "/healthcheck" "Incorrect path on liveliness http probe"
        Expect.equal livelinessProbe.HttpGet.Port 8080 "Incorrect port on liveliness http probe"
        Expect.equal livelinessProbe.HttpGet.Scheme "https" "Incorrect scheme on liveliness http probe"
        Expect.equal livelinessProbe.PeriodSeconds.Value 30 "Incorrect period on liveliness probe"
        Expect.equal livelinessProbe.FailureThreshold.Value 10 "Incorrect failure threshold on liveliness probe"
        let readinessProbe = cg.Containers.[0].ReadinessProbe
        Expect.isNotNull readinessProbe "Resulting container should have a readiness probe"
        Expect.equal readinessProbe.HttpGet.Path "/healthcheck" "Incorrect path on readiness http probe"
        Expect.equal readinessProbe.HttpGet.Port 8080 "Incorrect port on readiness http probe"
        Expect.equal readinessProbe.HttpGet.Scheme "https" "Incorrect scheme on readiness http probe"
        Expect.equal readinessProbe.InitialDelaySeconds.Value 30 "Incorrect initial delay threshold on readiness probe"
        Expect.equal readinessProbe.FailureThreshold.Value 5 "Incorrect failure threshold on readiness probe"
    }
    test "Container network profile with vnet has expected dependsOn" {
        let template =
            arm {
                add_resources [
                    vnet {
                        name "containernet"
                        add_address_spaces [
                            "10.30.40.0/20"
                        ]
                        add_subnets [
                            subnet {
                                name "ContainerSubnet"
                                prefix "10.40.41.0/24"
                                add_delegations [ SubnetDelegationService.ContainerGroups ]
                            }
                        ]
                    }
                    networkProfile {
                        name "netprofile"
                        vnet "containernet"
                        subnet "ContainerSubnet"
                    }
                    containerGroup {
                        name "appWithHttpFrontend"
                        operating_system Linux
                        restart_policy AlwaysRestart
                        add_instances [ nginx ]
                        network_profile "netprofile"
                    }
                ]
            }
        let json = template.Template |> Writer.toJson
        let jobj = Newtonsoft.Json.Linq.JObject.Parse(json)
        let expectedContainerNetDeps = "[resourceId('Microsoft.Network/virtualNetworks', 'containernet')]"
        let dependsOn = jobj.SelectToken("resources[?(@.name=='netprofile')].dependsOn")
        Expect.hasLength dependsOn 1 "netprofile has wrong number of dependencies"
        let actualContainerNetDeps =
            (dependsOn :?> Newtonsoft.Json.Linq.JArray).First.ToString()
        Expect.equal actualContainerNetDeps expectedContainerNetDeps "Dependencies didn't match"
    }
    test "Container network profile with linked vnet has empty dependsOn" {
        let template =
            arm {
                add_resources [
                    networkProfile {
                        name "netprofile"
                        link_to_vnet "containernet"
                        subnet "ContainerSubnet"
                    }
                    containerGroup {
                        name "appWithHttpFrontend"
                        operating_system Linux
                        restart_policy AlwaysRestart
                        add_instances [ nginx ]
                        network_profile "netprofile"
                    }
                ]
            }
        let json = template.Template |> Writer.toJson
        let jobj = Newtonsoft.Json.Linq.JObject.Parse(json)
        let dependsOn = jobj.SelectToken("resources[?(@.name=='netprofile')].dependsOn")
        Expect.hasLength dependsOn 0 "network profile had dependencies when existing vnet was linked"
    }
    test "Can link a network profile directly to a container group" {
        let profile = networkProfile { name "netprofile" }
        let template =
            containerGroup {
                name "appWithHttpFrontend"
                network_profile profile
            } |> asAzureResource

        Expect.equal "[resourceId('Microsoft.Network/networkProfiles', 'netprofile')]" template.NetworkProfile.Id "Incorrect profile name"
    }
]
