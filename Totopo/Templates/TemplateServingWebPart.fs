namespace Totopo.Templates

open Stubble.Core.Builders
open Stubble.Core.Loaders
open Stubble.Core.Settings
open Suave
open System
open System.IO
open System.Text.RegularExpressions
open FSharpPlus.Operators
open Totopo.Filesystem

module TemplateServing =
    type TemplateLoader = TemplatePath -> TemplateContent option

    let readTemplateFromDisk (directories: TemplatesDirectory list) (name: TemplatePath) =
        let resolveAbsoluteTemplatePath (directory: TemplatesDirectory) (templateName: TemplatePath) =
            let fileName =
                TemplatePath.value templateName + ".mustache"
            let dir = TemplatesDirectory.value directory
            dir + fileName

        let readFile filePath =
            printfn "filePath: %O" filePath
            match File.Exists(filePath) with
            | false -> None
            | true -> FileBytes(File.ReadAllText(filePath)) |> Some

        let readTemplateContent =
            readFile >=> (TemplateContent.fromBytes >> Some)

        let findFirst (found: TemplateContent option) (directory: TemplatesDirectory) =
            match found with
            | Some c -> Some c
            | None ->
                resolveAbsoluteTemplatePath directory name
                |> readTemplateContent

        fold findFirst None directories

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
        printfn "possibleParentPaths: %O" possibleParentPaths

        let loadTemplate (found: Template option) (parentPath: TemplatePath): Template option =
            printfn "found: %O" found
            match found with
            | Some tmpl -> Some tmpl
            | None ->
                let nestedName = TemplatePath.concat parentPath name
                loader nestedName |> map (fun x -> { TemplatePath = nestedName; TemplateContent = x })

        let loadReferencedTemplates (loadedTemplate: LoadedTemplate) =
            printfn "loadedTemplate: %O" loadedTemplate
            let referenceRegex = Regex @"\{\{>\s*(\S+)\s*\}\}"
            
            let getReferences content =
                let matchReferences = TemplateContent.value >> referenceRegex.Matches
                let matches = matchReferences content
                let getCaptureGroup index =
                    let item = matches.Item index
                    let captureGroup = item.Groups.Item 1
                    captureGroup
                [ 0 .. matches.Count - 1 ] |> map getCaptureGroup

            let processCaptureGroup (loaded: LoadedTemplate) (group: Group): LoadedTemplate =
                let referencedName = TemplatePath.fromString group.Value
                let referenced = readAllTemplates loader referencedName (Some loaded.Template.TemplatePath)
                let childTemplates = map (fun x -> x.Template :: x.ReferencedTemplates) referenced |> Option.orElse (Some []) |> Option.get
                let joinedTemplated = List.append loaded.ReferencedTemplates childTemplates
                let updatedContent =
                    match referenced with
                    | None -> loaded.Template.TemplateContent
                    | Some t ->
                        let realNameStr = TemplatePath.value t.Template.TemplatePath
                        let replaceContent = TemplateContent.replaceWith loaded.Template.TemplateContent
                        replaceContent group.Index group.Length realNameStr
                { loaded with
                    ReferencedTemplates = joinedTemplated
                    Template = { loaded.Template with TemplateContent = updatedContent }}
            
            let matches = getReferences loadedTemplate.Template.TemplateContent |> rev
            fold processCaptureGroup loadedTemplate matches

        fold loadTemplate None possibleParentPaths
        |> map (fun x -> {Template = x; ReferencedTemplates = []})
        |> map loadReferencedTemplates

    type View() =
        member this.StaticResource = Func<string, obj>((fun x -> x :> obj))

    let evaluateTemplate (loader: TemplateLoader) (name: TemplatePath): string option =
        let response = readAllTemplates loader name None
        match response with
        | Some template ->
            let templateToTuple template =
                (TemplatePath.value template.TemplatePath, TemplateContent.value template.TemplateContent)

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
                TemplateContent.value template.Template.TemplateContent

            stubble.Render(content, View()) |> Some
        | None -> None

    let serveTemplate (loader: TemplateLoader) (context: HttpContext): Async<HttpContext option> =
        async {
            let name = TemplatePath.fromRequest context.request
            printfn "name: %O" name

            return! match evaluateTemplate loader name with
                    | Some rendered -> Successful.OK rendered context
                    | None -> Async.result None
        }
