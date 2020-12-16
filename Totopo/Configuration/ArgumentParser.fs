// Copyright 2020 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Totopo.Configuration

open Argu
open Suave
open System.IO
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
        let configurationReader =
            match File.Exists(fullApplicationConfigPath) with
            | true -> ConfigurationReader.FromAppSettingsFile(fullApplicationConfigPath)
            | false -> ConfigurationReader.FromAppSettings()
        let argumentParser = ArgumentParser.Create<CliArguments>(programName = programName)
        let results = argumentParser.Parse(argv, configurationReader = configurationReader, ignoreUnrecognized = true)
        let httpPort = results.GetResult(CliArguments.HttpPort)
        let totopoResourcesPath = results.GetResult(CliArguments.TotopoResourcesPath)
        let applicationResourcesPath = results.GetResult(CliArguments.ApplicationResourcesPath)

        { HttpPort = httpPort; ApplicationResourcesPath = applicationResourcesPath; TotopoResourcesPath = totopoResourcesPath }
