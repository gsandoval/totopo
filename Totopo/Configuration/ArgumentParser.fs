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
        | [<CustomAppSettings "CloudProjectName"; CustomCommandLine "--cloud-project-name">] CloudProjectName of
            name: string
        | [<CustomAppSettings "CloudResourceType"; CustomCommandLine "--cloud-resource-type">] CloudResourceType of
            name: string
        | [<CustomAppSettings "CloudService"; CustomCommandLine "--cloud-service">] CloudService of name: string option
        | [<CustomAppSettings "CloudVersion"; CustomCommandLine "--cloud-version">] CloudVersion of name: string option
        | [<CustomAppSettings "CloudRegion"; CustomCommandLine "--cloud-region">] CloudRegion of name: string option
        | [<CustomAppSettings "LoggingMinLevel"; CustomCommandLine "--logging-min-level">] LoggingMinLevel of
            level: string
        | [<CustomAppSettings "AlsoLogToConsole"; CustomCommandLine "--also-log-to-console">] AlsoLogToConsole of console: bool
        | [<CustomAppSettings "ServingStrategy"; CustomCommandLine "--serving-strategy">] ServingStrategy of strategy: string

        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | HttpPort _ -> "Specify a port for the HTTP interface."
                | TotopoResourcesPath _ -> "Local disk path for generic totopo resources."
                | ApplicationResourcesPath _ -> "Local disk path for application resources."
                | ResourcesBucketBaseUri _ -> "Bucket uri for application resources."
                | ResourcesCdnBaseUrl _ -> "CDN url for application resources."
                | TemplateCachingTimeout _ -> "Maximum time a template should be kept in the cache."
                | CloudProjectName _ -> "Cloud project name."
                | CloudResourceType _ -> "Cloud resource type."
                | CloudService _ -> "Cloud service."
                | CloudVersion _ -> "Cloud version."
                | CloudRegion _ -> "Cloud region."
                | LoggingMinLevel _ -> "Minimum logging level that is output."
                | AlsoLogToConsole _ -> "Signal whether to log to console too. Defaults to false."
                | ServingStrategy _ -> "Serving strategy; 'local' prioritizes local disk, 'remote' prioritizes Cloud storage. Defaults to 'remote'."

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
            argumentParser.Parse(argv, configurationReader = configurationReader, ignoreUnrecognized = true, ignoreMissing = true)

        let httpPort = results.GetResult(HttpPort)

        let totopoResources =
            LocalResourcePath(results.GetResult(TotopoResourcesPath))

        let applicationCustomResources =
            LocalResourcePath(results.GetResult(ApplicationResourcesPath))

        let resourcesBucket =
            BucketBaseUri(results.GetResult(ResourcesBucketBaseUri))

        let resourcesCdn =
            CdnBaseUrl(results.GetResult(ResourcesCdnBaseUrl))

        let templateCachingTimeoutStr =
            results.GetResult(TemplateCachingTimeout)

        let templateCachingTimeout =
            TimeSpan.Parse(templateCachingTimeoutStr)

        let cloudProjectName = results.GetResult(CloudProjectName)
        let cloudResourceType = results.GetResult(CloudResourceType)
        let cloudService = results.GetResult(CloudService, None)
        let cloudVersion = results.GetResult(CloudVersion, None)
        let cloudRegion = results.GetResult(CloudRegion, None)

        let loggingMinLevel =
            Logging.LogLevel.ofString (results.GetResult(LoggingMinLevel))

        let alsoLogToConsole = results.GetResult(AlsoLogToConsole, false)

        let servingStrategyStr = results.GetResult(ServingStrategy, "remote")
        let servingStrategy = match servingStrategyStr with
                              | "local" -> Local
                              | "remote" -> Remote
                              | _ -> Remote

        { HttpPort = httpPort
          LocalResources =
              { ApplicationCustom = applicationCustomResources
                Totopo = totopoResources }
          ExternalResources =
              { BucketBase = resourcesBucket
                CdnBase = resourcesCdn }
          TemplateCachingTimeout = templateCachingTimeout
          CloudProject = 
              { Name = cloudProjectName
                ResourceType = cloudResourceType
                Region = cloudRegion
                Service = cloudService
                Version = cloudVersion}
          LoggingMinLevel = loggingMinLevel
          AlsoLogToConsole = alsoLogToConsole
          ServingStrategy = servingStrategy }
