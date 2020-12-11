namespace Totopo.Configuration

open Argu
open Suave
open System.Net
open System.Reflection

module ArgumentParser =
    [<GatherAllSources>]
    type CliArguments =
        | [<CustomAppSettings "HttpPort"; CustomCommandLine "--http-port">] HttpPort of port:Sockets.Port
        | [<CustomAppSettings "TotopoResourcesPath"; CustomCommandLine "--totopo-resources-path">] TotopoResourcesPath of path:string
        | [<CustomAppSettings "ApplicationResourcesPath"; CustomCommandLine "--application-resources-path">] ApplicationResourcesPath of path:string
        
        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | HttpPort _ -> "Specify a port for the HTTP interface"
                | TotopoResourcesPath _ -> "Specify the directory for generic totopo resources"
                | ApplicationResourcesPath _ -> "Specify the directory for application resources"

    let parse (argv: string[]) (assembly: Assembly): Configuration =
        let assemblyPath = assembly.Location
        let fullApplicationConfigPath = assemblyPath.[0..assemblyPath.Length-5] + ".config"
        let programName = assembly.FullName
        let configurationReader = ConfigurationReader.FromAppSettingsFile(fullApplicationConfigPath)
        let argumentParser = ArgumentParser.Create<CliArguments>(programName = programName)
        let results = argumentParser.Parse(argv, ignoreUnrecognized = true, configurationReader = configurationReader)
        let httpPort = results.GetResult(CliArguments.HttpPort)
        let totopoResourcesPath = results.GetResult(CliArguments.TotopoResourcesPath)
        let applicationResourcesPath = results.GetResult(CliArguments.ApplicationResourcesPath)

        { HttpPort = httpPort; ApplicationResourcesPath = applicationResourcesPath; TotopoResourcesPath = totopoResourcesPath }