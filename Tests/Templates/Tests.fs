module Tests

open Expecto
open Expecto.Flip
open FSharpPlus.Operators
open Totopo.Templates
open Totopo.Filesystem
open Suave

let fakeTestTemplates (name: TemplatePath): TemplateContent option =
    let fileBytes =
        match TemplatePath.value name with
        | "/hello" -> FileBytes "hello world" |> Some
        | "/chunk1" -> FileBytes "::chunk1" |> Some
        | "/chunk5" -> FileBytes "::chunk5" |> Some
        | "/index" -> FileBytes "::index {{> chunk1}}" |> Some
        | "/same-level-deps/chunk1" -> FileBytes "::projects::chunk1" |> Some
        | "/same-level-deps/chunk3" -> FileBytes "::projects::chunk3 {{> chunk4}}" |> Some
        | "/same-level-deps/chunk4" -> FileBytes "::projects::chunk4 {{> chunk5}}" |> Some
        | "/same-level-deps/2020/chunk1" -> FileBytes "::projects::2020::chunk1 {{> chunk2}}" |> Some
        | "/same-level-deps/2020/chunk2" -> FileBytes "::projects::2020::chunk2 {{> chunk3}}" |> Some
        | "/same-level-deps/2020/index" -> FileBytes "::projects::2020::index {{> chunk1}}" |> Some
        | _ -> None

    map TemplateContent.fromBytes fileBytes

[<Tests>]
let tests =
    testList
        "Templates.Serving"
        [ test "load hello world" {
              TemplatePath.fromString "hello"
              |> TemplateServing.evaluateTemplate fakeTestTemplates
              |> Expect.equal "loads hello world template" (Some "hello world")
          }

          test "load templates with same level deps" {
              TemplatePath.fromString "index"
              |> TemplateServing.evaluateTemplate fakeTestTemplates
              |> Expect.equal "loads template with same level deps" (Some "::index ::chunk1")
          }

          test "load templates with mixed level deps" {
              let expectedResult = "::projects::2020::index ::projects::2020::chunk1 ::projects::2020::chunk2 ::projects::chunk3 ::projects::chunk4 ::chunk5"
              TemplatePath.fromString "same-level-deps/2020/index"
              |> TemplateServing.evaluateTemplate fakeTestTemplates
              |> Expect.equal "loads template with same level deps" (Some expectedResult)
          }

          test "generate sub paths" {
              TemplateServing.generatePossibleReferencedPaths (TemplatePath.fromString "hello")
              |> Expect.equal "generates 2 options with simple name" [ TemplatePath.fromString "hello"; TemplatePath.empty ]

              TemplateServing.generatePossibleReferencedPaths (TemplatePath.fromString "hello/world")
              |> Expect.equal "generates 3 options with nested name" [ TemplatePath.fromString "hello/world"; TemplatePath.fromString "hello"; TemplatePath.empty ]

              TemplateServing.generatePossibleReferencedPaths TemplatePath.empty
              |> Expect.equal "generates 1 option none" [ TemplatePath.empty ]
          } ]

[<Tests>]
let typeTests =
    testList
        "Templates.TemplateName"
        [ test "fromRequest index" {
              let expectName message path =
                  let expectedName = TemplatePath.fromString path
                  Expect.equal message expectedName

              TemplatePath.fromRequest { HttpRequest.empty with rawPath = "" }
              |> expectName "replace empty path with index" "index"

              TemplatePath.fromRequest { HttpRequest.empty with rawPath = "/" }
              |> expectName "replace single slash with index" "index"

              TemplatePath.fromRequest
                  { HttpRequest.empty with
                        rawPath = "something/something/" }
              |> expectName "append index to directory" "something/something/index"
          }

          test "fromRequest cleanUp" {
              let expectName message path =
                  let expectedName = TemplatePath.fromString path
                  Expect.equal message expectedName

              TemplatePath.fromRequest
                  { HttpRequest.empty with
                        rawPath = "/aaa" }
              |> expectName "remove leading slash" "aaa"

              TemplatePath.fromRequest
                  { HttpRequest.empty with
                        rawPath = "/aaa/bbb/ccc" }
              |> expectName "remove leading slash on deep path" "aaa/bbb/ccc"

              TemplatePath.fromRequest
                  { HttpRequest.empty with
                        rawPath = "something/something/page.html" }
              |> expectName "remove html extension" "something/something/page"

              TemplatePath.fromRequest
                  { HttpRequest.empty with
                        rawPath = "something/something/style.css" }
              |> expectName "keep non html extension" "something/something/style.css"
          } ]
