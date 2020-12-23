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