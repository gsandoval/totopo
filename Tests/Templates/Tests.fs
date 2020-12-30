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

module Tests

open Expecto
open Expecto.Flip
open FSharpPlus.Operators
open Totopo.Configuration
open Totopo.Templates
open Totopo.Filesystem
open Suave
open System

let fakeTestTemplates (name: TemplatePath): TemplateText option =
    let contents =
        match TemplatePath.value name with
        | "/hello" ->
            FileContents.fromText "hello world" DateTime.Now DateTime.Now
            |> Some
        | "/chunk1" ->
            FileContents.fromText "::chunk1" DateTime.Now DateTime.Now
            |> Some
        | "/chunk5" ->
            FileContents.fromText "::chunk5" DateTime.Now DateTime.Now
            |> Some
        | "/index" ->
            FileContents.fromText "::index {{> chunk1}}" DateTime.Now DateTime.Now
            |> Some
        | "/same-level-deps/chunk1" ->
            FileContents.fromText "::projects::chunk1" DateTime.Now DateTime.Now
            |> Some
        | "/same-level-deps/chunk3" ->
            FileContents.fromText "::projects::chunk3 {{> chunk4}}" DateTime.Now DateTime.Now
            |> Some
        | "/same-level-deps/chunk4" ->
            FileContents.fromText "::projects::chunk4 {{> chunk5}}" DateTime.Now DateTime.Now
            |> Some
        | "/same-level-deps/2020/chunk1" ->
            FileContents.fromText "::projects::2020::chunk1 {{> chunk2}}" DateTime.Now DateTime.Now
            |> Some
        | "/same-level-deps/2020/chunk2" ->
            FileContents.fromText "::projects::2020::chunk2 {{> chunk3}}" DateTime.Now DateTime.Now
            |> Some
        | "/same-level-deps/2020/index" ->
            FileContents.fromText "::projects::2020::index {{> chunk1}}" DateTime.Now DateTime.Now
            |> Some
        | _ -> None

    map TemplateText.fromFileContents contents

let fakeCdnBaseUrl = CdnBaseUrl("")

[<Tests>]
let tests =
    testList
        "Templates.Serving"
        [ test "load hello world" {
            TemplatePath.fromString "hello"
            |> TemplateServing.evaluateTemplate fakeTestTemplates fakeCdnBaseUrl
            |> Expect.equal "loads hello world template" (Some "hello world")
          }

          test "load templates with same level deps" {
              TemplatePath.fromString "index"
              |> TemplateServing.evaluateTemplate fakeTestTemplates fakeCdnBaseUrl
              |> Expect.equal "loads template with same level deps" (Some "::index ::chunk1")
          }

          test "load templates with mixed level deps" {
              let expectedResult =
                  "::projects::2020::index ::projects::2020::chunk1 ::projects::2020::chunk2 ::projects::chunk3 ::projects::chunk4 ::chunk5"

              TemplatePath.fromString "same-level-deps/2020/index"
              |> TemplateServing.evaluateTemplate fakeTestTemplates fakeCdnBaseUrl
              |> Expect.equal "loads template with same level deps" (Some expectedResult)
          }

          test "generate sub paths" {
              TemplateServing.generatePossibleReferencedPaths (TemplatePath.fromString "hello")
              |> Expect.equal
                  "generates 2 options with simple name"
                  [ TemplatePath.fromString "hello"
                    TemplatePath.empty ]

              TemplateServing.generatePossibleReferencedPaths (TemplatePath.fromString "hello/world")
              |> Expect.equal
                  "generates 3 options with nested name"
                  [ TemplatePath.fromString "hello/world"
                    TemplatePath.fromString "hello"
                    TemplatePath.empty ]

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
