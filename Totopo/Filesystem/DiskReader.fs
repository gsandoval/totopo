namespace Totopo.Filesystem

open System.IO

module DiskReader =
    let readFile (filePath: FilePath) =
        let filePathStr = FilePath.value filePath
        match File.Exists(filePathStr) with
        | false -> None
        | true -> FileBytes(File.ReadAllText(filePathStr)) |> Some