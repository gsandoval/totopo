namespace Totopo.Filesystem

type FileBytes =
    | FileBytes of string

module FileBytes =
    let bytes (FileBytes str) = str