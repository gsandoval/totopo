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

open FSharp.Data
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
    let getContainerMetadata metadataPath =
        let url =
            "http://metadata.google.internal" + metadataPath

        let syncResult =
            Http.AsyncRequestString(url, headers = [ "Metadata-Flavor", "Google" ])
            |> Async.Catch
            |> Async.RunSynchronously

        match syncResult with
        | Choice1Of2 result -> Some result
        | Choice2Of2 err ->
            printfn "%O" err
            None

    let cloudClient =
        try
            LoggingServiceV2Client.Create() |> Some
        with e ->
            printfn "Failed to create Cloud Logging client %s" e.Message
            None

    let location =
        let location = configuration.CloudProject.Location

        let location =
            Option.orElse (getContainerMetadata "/computeMetadata/v1/instance/region") location

        Option.defaultValue "local-location" location

    let job =
        let job = configuration.CloudProject.Job
        Option.defaultValue "local-job" job

    let taskId =
        let taskId = configuration.CloudProject.TaskId

        let taskId =
            Option.orElse (getContainerMetadata "/computeMetadata/v1/instance/id") taskId

        Option.defaultValue "local-location" taskId

    let monitoredResource =
        MonitoredResource(Type = configuration.CloudProject.ResourceType)

    monitoredResource.Labels.Add("projectId", configuration.CloudProject.Name)
    monitoredResource.Labels.Add("location", location)
    monitoredResource.Labels.Add("task_id", taskId)
    monitoredResource.Labels.Add("job", job)

    let cloudSettings =
        { ProjectName = configuration.CloudProject.Name
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

let createLocalDiskTemplateServing
    (logger: TotopoLogger)
    (configuration: Configuration)
    (diskReader: FileReader)
    =
    let diskTemplateLoader =
        ComposeableTemplateLoader.readTemplate diskReader

    let cdnUri = getCdnBaseUri configuration
    TemplateServing.serveTemplate logger diskTemplateLoader cdnUri

let createCloudStorageClient (logger: TotopoLogger) =
    let storageClient =
        try
            StorageClient.Create() |> Some
        with e ->
            logger
                .At(Error)
                .With(e)
                .Log("Failed to create Cloud Storage client: {message}", setField "message" e.Message)

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
    (logger: TotopoLogger)
    (configuration: Configuration)
    (cloudFileReader: FileReader)
    =
    let remoteTemplateLoader =
        ComposeableTemplateLoader.readTemplate cloudFileReader

    let cdnUri = getCdnBaseUri configuration
    TemplateServing.serveTemplate logger remoteTemplateLoader cdnUri

[<EntryPoint>]
let main argv =
    let assembly = Assembly.GetExecutingAssembly()
    let configuration = ArgumentParser.parse argv assembly

    let loggerFactory = createLoggerFactory configuration

    let logger = loggerFactory.Create()

    logger
        .At(Info)
        .Log("Configuration loaded {configuration}", setField "configuration" configuration)

    let cts = new CancellationTokenSource()

    let conf =
        serverConfig cts.Token configuration.HttpPort loggerFactory.SuaveLogger

    let storageClient = createCloudStorageClient logger

    let diskFileReaderFactory = createDiskDirectoryReader configuration

    let cloudFileReaderFactory =
        createCloudDirectoryReader storageClient logger configuration

    let staticContentPathHandlers =
        let localResources = configuration.LocalResources

        let serveApplicationResource =
            Files.browse (LocalResourcePath.value localResources.ApplicationCustom)

        let serveTotopoResource =
            Files.browse (LocalResourcePath.value localResources.Totopo)

        [ Filters.pathRegex "/static/.*"
          >=> serveApplicationResource
          Filters.pathRegex "/static/.*"
          >=> serveTotopoResource ]

    let templatePathHandlers =
        let templateDiskReader = diskFileReaderFactory "templates"
        let templateCloudReader = cloudFileReaderFactory "templates"

        let serveTemplateFromDisk =
            createLocalDiskTemplateServing logger configuration templateDiskReader

        let serveTemplateFromCloudStorage =
            createCloudStorageTemplateServing logger configuration templateCloudReader

        match configuration.ServingStrategy with
        | Local ->
            [ serveTemplateFromDisk
              serveTemplateFromCloudStorage ]
        | Remote ->
            [ serveTemplateFromCloudStorage
              serveTemplateFromDisk ]

    let redirectHandlers =
        let routingDiskReader = diskFileReaderFactory "routing"
        let routingCloudReader = cloudFileReaderFactory "routing"

        match configuration.ServingStrategy with
        | Local -> [ RedirectHandler.handle logger routingDiskReader ]
        | Remote -> [ RedirectHandler.handle logger routingCloudReader ]

    let templatePathErrorHandlers =
        let errorHandler = TemplateServing.handleError logger
        List.map errorHandler templatePathHandlers

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

        logger
            .At(Info)
            .Log("Start stats: {stats}", (setField "stats" textStats))

    AppDomain.CurrentDomain.ProcessExit.Add
        (fun _ ->
            logger.At(Info).Log("Shutting down server")
            cts.Cancel())

    Console.CancelKeyPress.Add
        (fun _ ->
            logger.At(Info).Log("Shutting down server")
            cts.Cancel())

    serverTask.Wait()

    0 // return an integer exit code
