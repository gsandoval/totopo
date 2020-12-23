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