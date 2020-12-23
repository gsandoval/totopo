open System
open System.Net
open System.IO
open System.Reflection
open System.Threading
open Suave
open Suave.Logging
open Suave.Operators
open Totopo.Configuration
open Totopo.Templates

let logger = Targets.create Verbose [||]

let serverConfig cancellationToken httpPort =
    let staticFilesDirectory =
        Path.Combine(Directory.GetCurrentDirectory(), "resources", "totopo")

    { defaultConfig with
          bindings = [ HttpBinding.create HTTP IPAddress.Any httpPort ]
          cancellationToken = cancellationToken
          logger = logger
          homeFolder = Some staticFilesDirectory }

[<EntryPoint>]
let main argv =
    let assembly = Assembly.GetExecutingAssembly()
    let configuration = ArgumentParser.parse argv assembly
    let cts = new CancellationTokenSource()
    let conf = serverConfig cts.Token configuration.HttpPort

    let localResources = configuration.LocalResources
    let serveApplicationResource = Files.browse (LocalResourcePath.value localResources.ApplicationCustom)
    let serveTotopoResource = Files.browse (LocalResourcePath.value localResources.Totopo)
    let localTemplatesDirectories = List.map TemplatesDirectory.fromLocalPath [configuration.LocalResources.ApplicationCustom; configuration.LocalResources.Totopo]
    let serveTemplateFromDisk = localTemplatesDirectories
                                |> DiskTemplateLoader.readTemplate
                                |>  TemplateServing.serveTemplate <| configuration.ExternalResources.CdnBase
    
    let remoteTemplatesDirectory = TemplatesDirectory.fromBucketUri configuration.ExternalResources.BucketBase
    let remoteTemplateLoader = GoogleStorageTemplateLoader.readTemplate configuration.ExternalResources.BucketBase remoteTemplatesDirectory
    let serveTemplateFromCloudStorage = remoteTemplateLoader
                                        |>  TemplateServing.serveTemplate <| configuration.ExternalResources.CdnBase

    let app =
        choose [ Filters.GET
                 >=> choose
                         [ Filters.pathRegex ".*"
                           >=> choose [ Filters.pathRegex "/static/.*" >=> serveApplicationResource
                                        Filters.pathRegex "/static/.*" >=> serveTotopoResource
                                        serveTemplateFromCloudStorage
                                        serveTemplateFromDisk
                                        RequestErrors.NOT_FOUND "Not found" ] ]
                 RequestErrors.NOT_FOUND "Unsupported request" ]

    let listening, server = startWebServerAsync conf app
    let serverTask = Async.StartAsTask(server, Tasks.TaskCreationOptions.None, cts.Token)

    listening
    |> Async.RunSynchronously
    |> printfn "start stats: %A"

    AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
        printfn "Shutting down server."
        cts.Cancel())
    
    serverTask.Wait()
    printfn "bye bye."

    0 // return an integer exit code
