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

open Google.Api
open Google.Cloud.Logging.V2
open Google.Cloud.Storage.V1
open Suave
open Suave.Operators
open System
open System.IO
open System.Net
open System.Reflection
open System.Threading
open Totopo.Configuration
open Totopo.Filesystem
open Totopo.Logging
open Totopo.Templates

let createLoggerFactory configuration =
    let cloudClient =
        try
            LoggingServiceV2Client.Create() |> Some
        with
        | e ->
            printfn "Failed to create Cloud Logging client %s" e.Message
            None
    let monitoredResource = MonitoredResource(Type = "global")

    let cloudSettings =
        { ProjectName = configuration.CloudProjectName
          CloudClient = cloudClient
          MonitoredResource = monitoredResource
          FallbackMode = Console }

    LoggerFactory("totopo", configuration.LoggingMinLevel, cloudSettings, configuration.AlsoLogToConsole)

let serverConfig cancellationToken httpPort suaveLogger =
    let staticFilesDirectory =
        Path.Combine(Directory.GetCurrentDirectory(), "resources", "totopo")

    { defaultConfig with
          bindings = [ HttpBinding.create HTTP IPAddress.Any httpPort ]
          cancellationToken = cancellationToken
          logger = suaveLogger
          homeFolder = Some staticFilesDirectory }

let createLocalDiskTemplateServing (local: LocalResourcePaths) (external: ExternalResourceUris) loggerFactory =
    let templateResourceDir =
        ResourceDirectory.fromLocalPath "templates"

    let localTemplatesDirectories =
        List.map
            templateResourceDir
            [ local.ApplicationCustom
              local.Totopo ]

    let diskFileReader =
        DiskReader.readFile localTemplatesDirectories

    let diskTemplateLoader =
        ComposeableTemplateLoader.readTemplate diskFileReader

    TemplateServing.serveTemplate diskTemplateLoader external.CdnBase

let createCloudStorageTemplateServing (external: ExternalResourceUris) (loggerFactory: LoggerFactory) =
    let remoteTemplatesDirectory =
        ResourceDirectory.fromBucketUri "templates" external.BucketBase

    let logger = loggerFactory.Create()
    let storageClient =
        try 
            StorageClient.Create() |> Some
        with
        | e -> 
            logger.Log(Error, "Failed to create Cloud Storage client: {message}", setField "message" e.Message)
            None

    let remoteFileReader = GoogleStorageReader(storageClient, logger, remoteTemplatesDirectory, external.BucketBase)

    let cache =
        TimeExpiringFileCache(TimeSpan.FromMinutes 1.0)

    let cachingFileReader =
        CachingFileReader.create cache remoteFileReader.ReadFile

    let remoteTemplateLoader =
        ComposeableTemplateLoader.readTemplate cachingFileReader

    TemplateServing.serveTemplate remoteTemplateLoader external.CdnBase

[<EntryPoint>]
let main argv =
    let assembly = Assembly.GetExecutingAssembly()
    let configuration = ArgumentParser.parse argv assembly

    let loggerFactory = createLoggerFactory configuration

    let logger = loggerFactory.Create()

    let cts = new CancellationTokenSource()

    let conf =
        serverConfig cts.Token configuration.HttpPort loggerFactory.SuaveLogger

    let serveTemplateFromDisk =
        createLocalDiskTemplateServing configuration.LocalResources configuration.ExternalResources loggerFactory

    let serveTemplateFromCloudStorage =
        createCloudStorageTemplateServing configuration.ExternalResources loggerFactory

    let localResources = configuration.LocalResources

    let serveApplicationResource =
        Files.browse (LocalResourcePath.value localResources.ApplicationCustom)

    let serveTotopoResource =
        Files.browse (LocalResourcePath.value localResources.Totopo)

    let app =
        choose [ Filters.GET
                 >=> choose [ Filters.pathRegex ".*"
                              >=> choose [ Filters.pathRegex "/static/.*"
                                           >=> serveApplicationResource
                                           Filters.pathRegex "/static/.*"
                                           >=> serveTotopoResource
                                           serveTemplateFromCloudStorage
                                           serveTemplateFromDisk
                                           TemplateServing.handleError serveTemplateFromCloudStorage
                                           TemplateServing.handleError serveTemplateFromDisk
                                           RequestErrors.NOT_FOUND "Not found" ] ]
                 RequestErrors.NOT_FOUND "Unsupported request" ]

    let listening, server = startWebServerAsync conf app

    let serverTask =
        Async.StartAsTask(server, Tasks.TaskCreationOptions.None, cts.Token)

    listening
    |> Async.RunSynchronously
    |> fun startedData ->
        let startDataToStr (startData: Tcp.StartedData) =
            $"Started at {startData.startCalledUtc} binding to {startData.binding.ip}:{startData.binding.port}"

        let textStats =
            match Option.map startDataToStr (Array.head startedData) with
            | Some c -> c
            | None -> "missing stats"

        logger.Log(Info, "Start stats: {stats}", (setField "stats" textStats))

    AppDomain.CurrentDomain.ProcessExit.Add
        (fun _ ->
            logger.Log(Info, "Shutting down server")
            cts.Cancel())

    Console.CancelKeyPress.Add
        (fun _ ->
            logger.Log(Info, "Shutting down server")
            cts.Cancel())

    serverTask.Wait()

    0 // return an integer exit code
