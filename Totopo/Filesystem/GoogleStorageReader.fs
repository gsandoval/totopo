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

open Google.Apis.Auth.OAuth2;
open Google.Cloud.Storage.V1;
open System.IO
open Totopo.Configuration

type BucketName =
    | BucketName of string

module GoogleStorageReader =
    let readFile (bucket: BucketBaseUri) (filePath: FilePath) =
        let filePathStr = FilePath.value filePath
        
        let credential = GoogleCredential.GetApplicationDefault()
        let storage = StorageClient.Create(credential)
        
        try
            let getOptions = GetObjectOptions()
            let readObject = storage.GetObject(BucketBaseUri.name bucket, filePathStr, getOptions)
            let downloadOptions = DownloadObjectOptions()
            let inMemoryStream = new MemoryStream()
            storage.DownloadObject(readObject, inMemoryStream, downloadOptions)
            let templateContent = System.Text.Encoding.Default.GetString(inMemoryStream.ToArray())
            FileBytes templateContent |> Some
        with
            | _ -> None