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

open Google.Apis.Auth.OAuth2
open Google.Cloud.Storage.V1
open System
open System.IO
open Totopo.Configuration
open Totopo.Logging

type BucketName = BucketName of string

type GoogleStorageReader(storageClient: StorageClient option, logger: TotopoLogger, directory: ResourceDirectory, bucket: BucketBaseUri) =
    let innerRead (storage: StorageClient) (filename: FilePath) =
        let fullPathStr =
            ResourceDirectory.value directory
            + FilePath.value filename

        logger.Log(Debug, "Attempting to read {filename}", setField "filename" fullPathStr)
        try
            let getOptions = GetObjectOptions()

            let readObject =
                storage.GetObject(BucketBaseUri.name bucket, fullPathStr, getOptions)

            let downloadOptions = DownloadObjectOptions()
            let inMemoryStream = new MemoryStream()
            storage.DownloadObject(readObject, inMemoryStream, downloadOptions)
            let fileBytes = inMemoryStream.ToArray()

            let updatedTime =
                match readObject.Updated.HasValue with
                | true -> readObject.Updated.Value
                | _ ->
                    match readObject.TimeCreated.HasValue with
                    | true -> readObject.TimeCreated.Value
                    | false -> DateTime.Now

            let templateContent =
                FileContents.fromBytes fileBytes updatedTime DateTime.Now
            
            templateContent |> Some
        with
        | :? Responses.TokenResponseException as e ->
            logger.Log(Warn, "Failed to read from bucket: {detail}", setField "detail" e.Message)
            None
        | :? Google.GoogleApiException as e ->
            match e.HttpStatusCode with
            | Net.HttpStatusCode.NotFound -> None
            | _ -> reraise()

    member this.ReadFile(filename: FilePath) =
        match storageClient with
        | Some client -> innerRead client filename
        | None -> None

        
