﻿// Copyright 2020 Google LLC
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
open Totopo.Routing
open Totopo.Templates

let createLoggerFactory configuration =
    let cloudClient =
        try
            LoggingServiceV2Client.Create() |> Some
        with e ->
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

let getCdnBaseUri (configuration: Configuration) =
    match configuration.ServingStrategy with
    | Local -> CdnBaseUrl("")
    | Remote -> configuration.ExternalResources.CdnBase

let createDiskDirectoryReader (configuration: Configuration) (directory: string) =
    let templateResourceDir =
        ResourceDirectory.fromLocalPath directory

    let localTemplatesDirectories =
        List.map
            templateResourceDir
            [ configuration.LocalResources.ApplicationCustom
              configuration.LocalResources.Totopo ]

    DiskReader.readFile localTemplatesDirectories

let createLocalDiskTemplateServing (configuration: Configuration) (diskReader: string -> FileReader) loggerFactory =
    let templateDiskReader = diskReader "templates"

    let diskTemplateLoader =
        ComposeableTemplateLoader.readTemplate templateDiskReader

    let cdnUri = getCdnBaseUri configuration
    TemplateServing.serveTemplate diskTemplateLoader cdnUri

let createCloudStorageClient (logger: TotopoLogger) =
    let storageClient =
        try
            StorageClient.Create() |> Some
        with e ->
            logger.Log(Error, "Failed to create Cloud Storage client: {message}", setField "message" e.Message)
            None
    storageClient

let createCloudDirectoryReader
    (storageClient)
    (logger: TotopoLogger)
    (configuration: Configuration)
    (directory: string)
    =
    let remoteDirectory =
        ResourceDirectory.fromBucketUri directory configuration.ExternalResources.BucketBase

    let remoteFileReader =
        GoogleStorageReader(storageClient, logger, remoteDirectory, configuration.ExternalResources.BucketBase)

    let cache =
        TimeExpiringFileCache(TimeSpan.FromMinutes 1.0)

    CachingFileReader.create cache remoteFileReader.ReadFile

let createCloudStorageTemplateServing
    (configuration: Configuration)
    (cloudFileReaderFactory: string -> FileReader)
    =
    let cachingFileReader = cloudFileReaderFactory "templates"

    let remoteTemplateLoader =
        ComposeableTemplateLoader.readTemplate cachingFileReader

    let cdnUri = getCdnBaseUri configuration
    TemplateServing.serveTemplate remoteTemplateLoader cdnUri

[<EntryPoint>]
let main argv =
    let assembly = Assembly.GetExecutingAssembly()
    let configuration = ArgumentParser.parse argv assembly

    let loggerFactory = createLoggerFactory configuration

    let logger = loggerFactory.Create()

    logger.Log(Info, "Configuration loaded {configuration}", setField "configuration" configuration)

    let cts = new CancellationTokenSource()

    let conf =
        serverConfig cts.Token configuration.HttpPort loggerFactory.SuaveLogger
    
    let storageClient = createCloudStorageClient logger

    let diskFileReaderFactory = createDiskDirectoryReader configuration
    let cloudFileReaderFactory = createCloudDirectoryReader storageClient logger configuration

    let serveTemplateFromDisk =
        createLocalDiskTemplateServing configuration diskFileReaderFactory loggerFactory

    let serveTemplateFromCloudStorage =
        createCloudStorageTemplateServing configuration cloudFileReaderFactory

    let localResources = configuration.LocalResources

    let serveApplicationResource =
        Files.browse (LocalResourcePath.value localResources.ApplicationCustom)

    let serveTotopoResource =
        Files.browse (LocalResourcePath.value localResources.Totopo)

    let staticContentPathHandlers =
        [ Filters.pathRegex "/static/.*"
          >=> serveApplicationResource
          Filters.pathRegex "/static/.*"
          >=> serveTotopoResource ]

    let templatePathHandlers =
        match configuration.ServingStrategy with
        | Local ->
            [ serveTemplateFromDisk
              serveTemplateFromCloudStorage ]
        | Remote ->
            [ serveTemplateFromCloudStorage
              serveTemplateFromDisk ]

    let routingDiskReader = diskFileReaderFactory "routing"
    let routingCloudReader = cloudFileReaderFactory "routing"

    let redirectHandlers =
        match configuration.ServingStrategy with
        | Local -> [ RedirectHandler.handle routingDiskReader ]
        | Remote -> [ RedirectHandler.handle routingCloudReader ]

    let templatePathErrorHandlers =
        List.map TemplateServing.handleError templatePathHandlers

    let systemNotFoundHandler = RequestErrors.NOT_FOUND "Not found"

    let pathHandlers =
        List.concat [ redirectHandlers
                      staticContentPathHandlers
                      templatePathHandlers
                      templatePathErrorHandlers
                      [ systemNotFoundHandler ] ]

    let app =
        choose [ Filters.GET
                 >=> choose [ Filters.pathRegex ".*" >=> choose pathHandlers ]
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
