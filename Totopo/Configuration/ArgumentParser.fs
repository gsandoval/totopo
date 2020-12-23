namespace Totopo.Configuration

open Argu
open Suave
open System.IO
open System.Net
open System.Reflection

module ArgumentParser =
    [<GatherAllSources>]
    type CliArguments =
        | [<CustomAppSettings "HttpPort"; CustomCommandLine "--http-port">] HttpPort of port: Sockets.Port
        | [<CustomAppSettings "TotopoResourcesPath"; CustomCommandLine "--totopo-resources-path">] TotopoResourcesPath of path: string
        | [<CustomAppSettings "ApplicationResourcesPath"; CustomCommandLine "--application-resources-path">] ApplicationResourcesPath of path: string
        | [<CustomAppSettings "ResourcesBucketBaseUri"; CustomCommandLine "--resources-bucket-base-uri">] ResourcesBucketBaseUri of path: string
        | [<CustomAppSettings "ResourcesCdnBaseUrl"; CustomCommandLine "--resources-cdn-base-url">] ResourcesCdnBaseUrl of path: string

        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | HttpPort _ -> "Specify a port for the HTTP interface"
                | TotopoResourcesPath _ -> "Local disk path for generic totopo resources"
                | ApplicationResourcesPath _ -> "Local disk path for application resources"
                | ResourcesBucketBaseUri _ -> "Bucket uri for application resources"
                | ResourcesCdnBaseUrl _ -> "CDN url for application resources"

    let parse (argv: string []) (assembly: Assembly): Configuration =
        let assemblyPath = assembly.Location

        let fullApplicationConfigPath =
            assemblyPath.[0..assemblyPath.Length - 5]
            + ".config"

        let programName = assembly.FullName

        let configurationReader =
            match File.Exists(fullApplicationConfigPath) with
            | true -> ConfigurationReader.FromAppSettingsFile(fullApplicationConfigPath)
            | false -> ConfigurationReader.FromAppSettings()

        let argumentParser =
            ArgumentParser.Create<CliArguments>(programName = programName)

        let results =
            argumentParser.Parse(argv, configurationReader = configurationReader, ignoreUnrecognized = true)

        let httpPort = results.GetResult(CliArguments.HttpPort)

        let totopoResources =
            LocalResourcePath(results.GetResult(CliArguments.TotopoResourcesPath))

        let applicationCustomResources =
            LocalResourcePath(results.GetResult(CliArguments.ApplicationResourcesPath))

        let resourcesBucket =
            BucketBaseUri(results.GetResult(CliArguments.ResourcesBucketBaseUri))

        let resourcesCdn =
            CdnBaseUrl(results.GetResult(CliArguments.ResourcesCdnBaseUrl))

        { HttpPort = httpPort
          LocalResources =
              { ApplicationCustom = applicationCustomResources
                Totopo = totopoResources }
          ExternalResources = { BucketBase = resourcesBucket; CdnBase = resourcesCdn } }
