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
open System
open System.IO
open System.Net
open System.Reflection

module ArgumentParser =
    [<GatherAllSources>]
    type CliArguments =
        | [<CustomAppSettings "HttpPort"; CustomCommandLine "--http-port">] HttpPort of port: Sockets.Port
        | [<CustomAppSettings "TotopoResourcesPath"; CustomCommandLine "--totopo-resources-path">] TotopoResourcesPath of
            path: string
        | [<CustomAppSettings "ApplicationResourcesPath"; CustomCommandLine "--application-resources-path">] ApplicationResourcesPath of
            path: string
        | [<CustomAppSettings "ResourcesBucketBaseUri"; CustomCommandLine "--resources-bucket-base-uri">] ResourcesBucketBaseUri of
            uri: string
        | [<CustomAppSettings "ResourcesCdnBaseUrl"; CustomCommandLine "--resources-cdn-base-url">] ResourcesCdnBaseUrl of
            url: string
        | [<CustomAppSettings "TemplateCachingTimeout"; CustomCommandLine "--template-caching-timeout">] TemplateCachingTimeout of
            timeout: string

        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | HttpPort _ -> "Specify a port for the HTTP interface"
                | TotopoResourcesPath _ -> "Local disk path for generic totopo resources"
                | ApplicationResourcesPath _ -> "Local disk path for application resources"
                | ResourcesBucketBaseUri _ -> "Bucket uri for application resources"
                | ResourcesCdnBaseUrl _ -> "CDN url for application resources"
                | TemplateCachingTimeout _ -> "Maximum time a template should be kept in the cache"

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

        let templateCachingTimeoutStr =
            results.GetResult(CliArguments.TemplateCachingTimeout)

        let templateCachingTimeout =
            TimeSpan.Parse(templateCachingTimeoutStr)

        { HttpPort = httpPort
          LocalResources =
              { ApplicationCustom = applicationCustomResources
                Totopo = totopoResources }
          ExternalResources =
              { BucketBase = resourcesBucket
                CdnBase = resourcesCdn }
          TemplateCachingTimeout = templateCachingTimeout }
