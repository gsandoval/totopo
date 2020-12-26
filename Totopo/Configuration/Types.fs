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

namespace Totopo.Configuration

open Suave
open System

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
      ExternalResources: ExternalResourceUris
      TemplateCachingTimeout: TimeSpan }
