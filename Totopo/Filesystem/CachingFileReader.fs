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
open System.Collections.Concurrent

type ExpirationPolicy = FileContents -> FileContents option

type TimeExpiringFileCache(ttl: TimeSpan) =
    let cache =
        ConcurrentDictionary<FilePath, FileContents>()

    member this.IsValid(contents: FileContents): FileContents option =
        match contents.LoadedAt < DateTime.Now - ttl with
        | true -> None
        | _ -> Some contents

    member this.Get(key: FilePath): FileContents option =
        let exists, value = cache.TryGetValue key

        match exists with
        | true -> this.IsValid value
        | _ -> None

    member this.Add (key: FilePath) (contents: FileContents) =
        cache.AddOrUpdate(key, contents, (fun _ _ -> contents))

module CachingFileReader =
    let create (cache: TimeExpiringFileCache) (fileReader: FileReader) =
        fun (filename: FilePath) ->
            let cached = cache.Get filename

            let result =
                match cached with
                | None -> fileReader filename
                | _ -> cached

            match result with
            | Some c -> cache.Add filename c |> Some
            | _ -> result
