namespace Totopo.Configuration

open Suave

type LocalResourcePath = LocalResourcePath of string

module LocalResourcePath =
    let value (LocalResourcePath str) = str

type LocalResourcePaths = {
    ApplicationCustom: LocalResourcePath
    Totopo: LocalResourcePath }

type BucketBaseUri = BucketBaseUri of string

module BucketBaseUri =
    let name (BucketBaseUri uri) =
        match uri.IndexOf('/') with
        | -1 -> uri
        | slashIndex -> uri.Substring(0, slashIndex)

    let path (BucketBaseUri uri) =
        match uri.IndexOf('/') with
        | -1 -> ""
        | slashIndex -> uri.Substring(slashIndex + 1)

type CdnBaseUrl = CdnBaseUrl of string

module CdnBaseUrl =
    let value (CdnBaseUrl str) = str

type ExternalResourceUris = {
    BucketBase: BucketBaseUri
    CdnBase: CdnBaseUrl }

type Configuration =
    { HttpPort: Sockets.Port
      LocalResources: LocalResourcePaths
      ExternalResources: ExternalResourceUris }