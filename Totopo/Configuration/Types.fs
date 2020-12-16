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
