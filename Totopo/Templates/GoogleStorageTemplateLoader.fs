namespace Totopo.Templates

open FSharpPlus.Operators
open Totopo.Configuration
open Totopo.Filesystem

module GoogleStorageTemplateLoader =
    let readTemplate (bucketName: BucketBaseUri) (templatesDirectory: TemplatesDirectory) (name: TemplatePath) =
        let readTemplateContent =
            (GoogleStorageReader.readFile bucketName) >=> (TemplateContent.fromBytes >> Some)

        let fileName = TemplatesDirectory.value templatesDirectory + TemplatePath.value name + ".mustache"
        let path = FilePath fileName
        readTemplateContent path