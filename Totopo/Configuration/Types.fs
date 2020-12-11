namespace Totopo.Configuration

open Suave
open System.IO
open Totopo.Templates

type Configuration = { HttpPort: Sockets.Port; ApplicationResourcesPath: string; TotopoResourcesPath: string }
    module Configuration =
        let templatesDirectories (config: Configuration) =
            let totopoTemplatesDirectory = Path.Combine(config.TotopoResourcesPath, "templates") 
                                           |> TemplatesDirectory.fromPath
            let applicationTemplatesDirectory = Path.Combine(config.ApplicationResourcesPath, "templates")
                                                |> TemplatesDirectory.fromPath
            [ applicationTemplatesDirectory; totopoTemplatesDirectory ]