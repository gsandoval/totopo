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

namespace Totopo.Filesystem

open System
open System.IO
open Totopo.Configuration

type FileContents =
    { Text: string
      LoadedAt: DateTime
      UpdatedAt: DateTime }

module FileContents =
    let text (contents: FileContents) = contents.Text

    let fromBytes (bytes: byte array) (updated: DateTime) (loaded: DateTime) =
        { Text = System.Text.Encoding.Default.GetString(bytes)
          UpdatedAt = updated
          LoadedAt = loaded }

    let fromText (text: string) (updated: DateTime) (loaded: DateTime) =
        { Text = text
          UpdatedAt = updated
          LoadedAt = loaded }

type FilePath = FilePath of string

module FilePath =
    let value (FilePath path) = path

type FileReader = FilePath -> FileContents option

type ResourceDirectory = private ResourceDirectory of string

module ResourceDirectory =
    let fromLocalPath (subPath: string) (path: LocalResourcePath) =
        let templatesPath =
            Path.Combine(LocalResourcePath.value path, subPath)

        ResourceDirectory templatesPath

    let fromBucketUri (subPath: string) (uri: BucketBaseUri) =
        let templatesPath = (BucketBaseUri.path uri) + subPath
        ResourceDirectory templatesPath

    let value (ResourceDirectory str) = str
