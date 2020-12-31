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

open FSharpPlus.Operators
open System.IO

module DiskReader =
    let readFile (directories: ResourceDirectory list) (filename: FilePath) =
        let filenameStr = FilePath.value filename

        let readFileFromFirstDirectoryMatch (found: FileContents option) (directory: ResourceDirectory) =
            match found with
            | Some c -> Some c
            | None ->
                let dir = ResourceDirectory.value directory
                let potentialPath = dir + filenameStr

                match File.Exists(potentialPath) with
                | false -> None
                | true ->
                    let fileText = File.ReadAllText potentialPath
                    let updatedAt = File.GetLastWriteTimeUtc(potentialPath)
                    FileContents.fromText fileText updatedAt System.DateTime.Now
                    |> Some

        fold readFileFromFirstDirectoryMatch None directories
