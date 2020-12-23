namespace Totopo.Filesystem

type FileBytes =
    | FileBytes of string

module FileBytes =
    let bytes (FileBytes str) = str

type FilePath =
    | FilePath of string

module FilePath =
    let value (FilePath path) = path

type FileReader = FilePath -> FileBytes option