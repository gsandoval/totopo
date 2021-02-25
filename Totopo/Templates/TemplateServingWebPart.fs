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
open Totopo.Logging

module TemplateServing =
    let rec generateAllPathsInTree (referencedBy: TemplatePath): TemplatePath list =
        match referencedBy with
        | emptyPath when emptyPath = TemplatePath.empty -> [ emptyPath ]
        | path ->
            path
            :: generateAllPathsInTree (TemplatePath.parent path)

    let rec generatePossibleReferencedPaths (referencedBy: TemplatePath list): TemplatePath list =
        match referencedBy with
        | [] -> [ TemplatePath.empty ]
        | [ current ] -> generateAllPathsInTree current
        | current :: rest ->
            current
            :: TemplatePath.parent current
               :: generatePossibleReferencedPaths rest

    let rec readAllTemplates
        (loader: TemplateLoader)
        (name: TemplatePath)
        (referencedBy: TemplatePath list)
        : LoadedTemplate option =
        let possibleParentPaths: TemplatePath list =
            generatePossibleReferencedPaths referencedBy

        let loadTemplate (found: Template option) (parentPath: TemplatePath): Template option =
            match found with
            | Some tmpl -> Some tmpl
            | None ->
                let nestedName = TemplatePath.concat parentPath name

                loader nestedName
                |> map
                    (fun x ->
                        { Path = nestedName
                          Contents = x
                          Id = TemplateId.generate () })

        let getContentMatches (regex: Regex) content =
            let matches = regex.Matches content.Text

            let getCaptureGroup index =
                let item = matches.Item index
                let captureGroup = item.Groups.Item 1
                captureGroup

            [ 0 .. matches.Count - 1 ]
            |> map getCaptureGroup
            |> rev

        let loadReferencedTemplates (loadedTemplate: LoadedTemplate) =
            let processCaptureGroup (loaded: LoadedTemplate) (group: Group): LoadedTemplate =
                let referencedName = TemplatePath.fromString group.Value

                let referenced =
                    readAllTemplates loader referencedName (loaded.Template.Path :: referencedBy)

                let childTemplates =
                    map (fun x -> x.Template :: x.ReferencedTemplates) referenced
                    |> Option.orElse (Some [])
                    |> Option.get

                let joinedTemplated =
                    List.append loaded.ReferencedTemplates childTemplates

                let updatedContent =
                    match referenced with
                    | None -> loaded.Template.Contents
                    | Some t ->
                        let realNameStr = TemplatePath.value t.Template.Path

                        let replaceContent =
                            TemplateContents.replaceWith loaded.Template.Contents

                        replaceContent group.Index group.Length realNameStr

                { loaded with
                      ReferencedTemplates = joinedTemplated
                      Template =
                          { loaded.Template with
                                Contents = updatedContent } }

            let referenceRegex = Regex @"\{\{>\s*(\S+)\s*\}\}"

            let referenceMatches =
                getContentMatches referenceRegex loadedTemplate.Template.Contents

            fold processCaptureGroup loadedTemplate referenceMatches

        let replaceSelfReferences (loadedTemplate: LoadedTemplate) =
            let processCaptureGroup (loaded: LoadedTemplate) (group: Group): LoadedTemplate =
                let replacedSelfReference =
                    group.Value.Replace(
                        "Templates.ThisTemplate",
                        "Templates.TemplatesById."
                        + (TemplateId.value loaded.Template.Id)
                    )

                let updatedContent =
                    TemplateContents.replaceWith loaded.Template.Contents group.Index group.Length replacedSelfReference

                { loaded with
                      Template =
                          { loaded.Template with
                                Contents = updatedContent } }

            let selfReferenceRegex =
                Regex @"\{\{\s*(Templates\.ThisTemplate\S*)\s*\}\}"

            let selfReferenceMatches =
                getContentMatches selfReferenceRegex loadedTemplate.Template.Contents

            fold processCaptureGroup loadedTemplate selfReferenceMatches

        fold loadTemplate None possibleParentPaths
        |> map
            (fun x ->
                { Template = x
                  ReferencedTemplates = [] })
        |> map loadReferencedTemplates
        |> map replaceSelfReferences

    let evaluateTemplate (loader: TemplateLoader) (cdnBase: CdnBaseUrl) (name: TemplatePath): string option =
        let response = readAllTemplates loader name []

        match response with
        | Some template ->
            let templateToTuple template =
                (TemplatePath.value template.Path, template.Contents.Text)

            let referencedTemplates =
                map templateToTuple template.ReferencedTemplates
                |> Map.ofList

            let configureStubble (settings: RendererSettingsBuilder): unit =
                let templateProvider = DictionaryLoader referencedTemplates

                settings.SetPartialTemplateLoader templateProvider
                |> ignore

                ()

            let stubble =
                StubbleBuilder()
                    .Configure(Action<RendererSettingsBuilder>(configureStubble))
                    .Build()

            let content = template.Template.Contents.Text

            stubble.Render(content, ViewModels.ViewMain(cdnBase, template))
            |> Some
        | None -> None

    let serveTemplate
        (logger: TotopoLogger)
        (loader: TemplateLoader)
        (cdnBase: CdnBaseUrl)
        (context: HttpContext)
        : Async<HttpContext option> =
        async {
            try
                let name = TemplatePath.fromRequest context.request
                let rendered = evaluateTemplate loader cdnBase name

                return!
                    match rendered with
                    | Some rendered ->
                        logger
                            .At(Info)
                            .Log(
                                "Serving {host}{path}",
                                setField "path" context.request.path
                                >> setField "host" context.request.host
                            )

                        Successful.OK rendered context
                    | None ->
                        logger
                            .At(Info)
                            .Log(
                                "Missed {host}{path}",
                                setField "path" context.request.path
                                >> setField "host" context.request.host
                            )

                        Async.result None
            with e ->
                let errorList =
                    match context.userState.TryGetValue "errors" with
                    | true, errorListObj -> (errorListObj :?> list<TemplateServingError>)
                    | _ -> []

                context.userState.Remove("errors") |> ignore
                let error: TemplateServingError = { Message = e.Message; InnerError = e }

                context.userState.Add("errors", List.append errorList [ error ])
                |> ignore

                return! Async.result None
        }

    let handleError (logger: TotopoLogger) (templateServing: WebPart) (context: HttpContext) =
        async {
            let context =
                match context.userState.TryGetValue "errors" with
                | true, errorList ->
                    let logError (err: TemplateServingError) =
                        logger
                            .At(Error)
                            .With(err.InnerError)
                            .Log("Failed to load template")

                    { context with
                          request =
                              { context.request with
                                    rawPath = "/errors/500" } }
                | _ ->
                    { context with
                          request =
                              { context.request with
                                    rawPath = "/errors/404" } }

            return! templateServing context
        }
