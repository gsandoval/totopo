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
          bindings = [ HttpBinding.create HTTP IPAddress.Loopback httpPort ]
          cancellationToken = cancellationToken
          logger = logger
          homeFolder = Some staticFilesDirectory }

[<EntryPoint>]
let main argv =
    let assembly = Assembly.GetExecutingAssembly()
    let configuration = ArgumentParser.parse argv assembly
    let cts = new CancellationTokenSource()
    let conf = serverConfig cts.Token configuration.HttpPort

    let serveApplicationResource = Files.browse configuration.ApplicationResourcesPath
    let serveTotopoResource = Files.browse configuration.TotopoResourcesPath
    let serveTemplateFromDisk = Configuration.templatesDirectories configuration
                                |> TemplateServing.readTemplateFromDisk
                                |>  TemplateServing.serveTemplate

    let app =
        choose [ Filters.GET
                 >=> choose
                         [ Filters.pathRegex ".*"
                           >=> choose [ Filters.pathRegex "/static/.*" >=> serveApplicationResource
                                        Filters.pathRegex "/static/.*" >=> serveTotopoResource
                                        serveTemplateFromDisk
                                        RequestErrors.NOT_FOUND "Not found" ] ]
                 RequestErrors.NOT_FOUND "Unsupported request" ]

    let listening, server = startWebServerAsync conf app
    Async.Start(server, cts.Token)

    listening
    |> Async.RunSynchronously
    |> printfn "start stats: %A"

    Console.Read() |> ignore

    cts.Cancel()
    0 // return an integer exit code
