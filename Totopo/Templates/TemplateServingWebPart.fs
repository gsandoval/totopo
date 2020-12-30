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

open Stubble.Core.Builders
open Stubble.Core.Loaders
open Stubble.Core.Settings
open Suave
open System
open System.Text.RegularExpressions
open FSharpPlus.Operators
open Totopo.Configuration

module TemplateServing =
    type LoadedTemplate =
        { Template: Template
          ReferencedTemplates: Template list }

    let rec generatePossibleReferencedPaths (referencedBy: TemplatePath): TemplatePath list =
            match referencedBy with
            | emptyPath when emptyPath = TemplatePath.empty -> [ emptyPath ]
            | path -> path :: generatePossibleReferencedPaths (TemplatePath.parent path)

    let rec readAllTemplates (loader: TemplateLoader)
                             (name: TemplatePath)
                             (referencedBy: TemplatePath option)
                             : LoadedTemplate option =
        let possibleParentPaths: TemplatePath list =
            let path = Option.orElse (Some TemplatePath.empty) referencedBy
            generatePossibleReferencedPaths path.Value

        let loadTemplate (found: Template option) (parentPath: TemplatePath): Template option =
            match found with
            | Some tmpl -> Some tmpl
            | None ->
                let nestedName = TemplatePath.concat parentPath name
                loader nestedName |> map (fun x -> { Path = nestedName; Text = x })

        let loadReferencedTemplates (loadedTemplate: LoadedTemplate) =
            let referenceRegex = Regex @"\{\{>\s*(\S+)\s*\}\}"

            let getReferences content =
                let matchReferences = TemplateText.value >> referenceRegex.Matches
                let matches = matchReferences content
                let getCaptureGroup index =
                    let item = matches.Item index
                    let captureGroup = item.Groups.Item 1
                    captureGroup
                [ 0 .. matches.Count - 1 ] |> map getCaptureGroup

            let processCaptureGroup (loaded: LoadedTemplate) (group: Group): LoadedTemplate =
                let referencedName = TemplatePath.fromString group.Value
                let referenced = readAllTemplates loader referencedName (Some loaded.Template.Path)
                let childTemplates = map (fun x -> x.Template :: x.ReferencedTemplates) referenced |> Option.orElse (Some []) |> Option.get
                let joinedTemplated = List.append loaded.ReferencedTemplates childTemplates
                let updatedContent =
                    match referenced with
                    | None -> loaded.Template.Text
                    | Some t ->
                        let realNameStr = TemplatePath.value t.Template.Path
                        let replaceContent = TemplateText.replaceWith loaded.Template.Text
                        replaceContent group.Index group.Length realNameStr
                { loaded with
                    ReferencedTemplates = joinedTemplated
                    Template = { loaded.Template with Text = updatedContent }}

            let matches = getReferences loadedTemplate.Template.Text |> rev
            fold processCaptureGroup loadedTemplate matches

        fold loadTemplate None possibleParentPaths
        |> map (fun x -> {Template = x; ReferencedTemplates = []})
        |> map loadReferencedTemplates

    type View(cdnBase: CdnBaseUrl) =
        member this.StaticResource = Func<string, obj>((fun x -> x :> obj))
        member this.static_resource = Func<string, obj>((fun x -> CdnBaseUrl.value cdnBase + x :> obj))

    let evaluateTemplate (loader: TemplateLoader) (cdnBase: CdnBaseUrl) (name: TemplatePath): string option =
        let response = readAllTemplates loader name None
        match response with
        | Some template ->
            let templateToTuple template =
                (TemplatePath.value template.Path, TemplateText.value template.Text)

            let referencedTemplates =
                map templateToTuple template.ReferencedTemplates
                |> Map.ofList

            let configureStubble (settings: RendererSettingsBuilder): unit =
                let templateProvider = DictionaryLoader referencedTemplates
                settings.SetPartialTemplateLoader templateProvider
                |> ignore
                ()

            let stubble =
                StubbleBuilder().Configure(Action<RendererSettingsBuilder>(configureStubble)).Build()

            let content =
                TemplateText.value template.Template.Text

            stubble.Render(content, View(cdnBase)) |> Some
        | None -> None

    let serveTemplate (loader: TemplateLoader) (cdnBase: CdnBaseUrl) (context: HttpContext): Async<HttpContext option> =
        async {
            try 
                let name = TemplatePath.fromRequest context.request
                let rendered = evaluateTemplate loader cdnBase name
                return! match rendered with
                        | Some rendered -> Successful.OK rendered context
                        | None -> Async.result None
            with
            | e ->
                printfn "%O" e
                let errorList = match context.userState.TryGetValue "errors" with
                                | true, errorListObj -> (errorListObj :?> list<TemplateServingError>)
                                | _ -> []
                context.userState.Remove("errors") |> ignore
                let error: TemplateServingError = { Message = e.Message; InnerError = e }
                context.userState.Add("errors", List.append errorList [error]) |> ignore
                return! Async.result None
        }

    let handleError (templateServing: WebPart) (context: HttpContext) =
        async {
            let context = match context.userState.TryGetValue "errors" with
                          | true, errorList ->
                            { context with request = { context.request with rawPath = "/errors/500" } }
                          | _ ->
                            { context with request = { context.request with rawPath = "/errors/404" } }
            return! templateServing context
        }