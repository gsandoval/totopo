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

open System
open System.Net
open System.IO
open System.Reflection
open System.Threading
open Suave
open Suave.Logging
open Suave.Operators
open Totopo.Configuration
open Totopo.Filesystem
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

    let templateResourceDir = ResourceDirectory.fromLocalPath "templates"
    let localTemplatesDirectories = List.map templateResourceDir [configuration.LocalResources.ApplicationCustom; configuration.LocalResources.Totopo]
    let diskFileReader = DiskReader.readFile localTemplatesDirectories
    let diskTemplateLoader = ComposeableTemplateLoader.readTemplate diskFileReader
    let serveTemplateFromDisk = TemplateServing.serveTemplate diskTemplateLoader configuration.ExternalResources.CdnBase
    
    let remoteTemplatesDirectory = ResourceDirectory.fromBucketUri "templates" configuration.ExternalResources.BucketBase
    let remoteFileReader = GoogleStorageReader.readFile remoteTemplatesDirectory configuration.ExternalResources.BucketBase
    let remoteTemplateLoader = ComposeableTemplateLoader.readTemplate remoteFileReader
    let serveTemplateFromCloudStorage = TemplateServing.serveTemplate remoteTemplateLoader configuration.ExternalResources.CdnBase

    let localResources = configuration.LocalResources
    let serveApplicationResource = Files.browse (LocalResourcePath.value localResources.ApplicationCustom)
    let serveTotopoResource = Files.browse (LocalResourcePath.value localResources.Totopo)

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
 
    System.Console.CancelKeyPress.Add(fun _ ->
        printfn "Shutting down server."
        cts.Cancel())

    serverTask.Wait()

    0 // return an integer exit code
