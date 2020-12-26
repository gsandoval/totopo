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

open Suave
open System.IO
open Totopo.Configuration
open Totopo.Filesystem

type TemplatePath = private TemplatePath of string

module TemplatePath =
    let fromRequest (request: HttpRequest) =
        let removeLeadingSlash (path: string) =
            match path.StartsWith("/") with
            | false -> path
            | true -> path.[1..]

        let removeHtmlExtension (path: string) =
            match path.EndsWith(".html") with
            | true -> path.[0..path.Length-6]
            | false -> path

        let convertDirectoryToIndex (path: string) =
            match path.EndsWith("/") || path.Length = 0 with
            | true -> path + "index"
            | false -> path

        request.path
        |> (removeLeadingSlash
            >> removeHtmlExtension
            >> convertDirectoryToIndex
            >> TemplatePath)

    let empty = TemplatePath ""

    let fromString (name: string) =
        name |> TemplatePath

    let value (TemplatePath str) = str

    let parent (path: TemplatePath) =
        let pathAsStr = value path
        match pathAsStr.Contains("/") with
        | false -> empty
        | true ->
            let parentStr = pathAsStr.Substring(0, pathAsStr.LastIndexOf("/"))
            TemplatePath parentStr

    let concat (parentPath: TemplatePath) (name: TemplatePath) =
        let parentAsStr = value parentPath
        let nameAsStr = value name
        fromString (parentAsStr + "/" + nameAsStr)

type TemplateText = private TemplateText of string

// Introduce FileBytes to push reading from disk out of this type
module TemplateText =
    let fromFileContents (contents: FileContents) =
        FileContents.text contents |> TemplateText

    let value (TemplateText str) = str

    let replaceWith (content: TemplateText) (index: int) (length: int) (updated: string) =
        let str = value content
        let updated = str.Substring(0, index) + updated + str.Substring(index + length)
        TemplateText updated

type Template =
    { Path: TemplatePath
      Text: TemplateText }

type TemplateLoader = TemplatePath -> TemplateText option