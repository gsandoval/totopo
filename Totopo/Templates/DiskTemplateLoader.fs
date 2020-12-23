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

namespace Totopo.Templates

open FSharpPlus.Operators
open Totopo.Filesystem

module DiskTemplateLoader =
    let readTemplate (directories: TemplatesDirectory list) (name: TemplatePath) =
        let resolveAbsoluteTemplatePath (directory: TemplatesDirectory) (templateName: TemplatePath) =
            let fileName =
                TemplatePath.value templateName + ".mustache"
            let dir = TemplatesDirectory.value directory
            FilePath(dir + fileName)

        let readTemplateContent =
            DiskReader.readFile >=> (TemplateContent.fromBytes >> Some)

        let findFirst (found: TemplateContent option) (directory: TemplatesDirectory) =
            match found with
            | Some c -> Some c
            | None ->
                resolveAbsoluteTemplatePath directory name
                |> readTemplateContent

        fold findFirst None directories