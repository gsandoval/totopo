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

namespace Totopo.Routing

open FSharpPlus.Operators
open Legivel.Attributes
open Legivel.Serialization
open Suave
open System.Text.RegularExpressions
open Totopo.Filesystem
open Totopo.Logging

module RedirectHandler =
    type RedirectOrigin =
        { [<YamlField(Name = "Host")>]
          Host: string
          [<YamlField(Name = "Path")>]
          Path: string }

    type RedirectDestination =
        { [<YamlField(Name = "Url")>]
          Url: string }

    type RedirectConfiguration =
        { [<YamlField(Name = "Origin")>]
          Origin: RedirectOrigin
          [<YamlField(Name = "Destination")>]
          Destination: RedirectDestination }

    type RedirectConfigurationFile =
        { [<YamlField(Name = "Redirects")>]
          Redirects: RedirectConfiguration list }

    let parseConfiguration (logger: TotopoLogger) (file: FileContents): RedirectConfigurationFile option =
        Deserialize<RedirectConfigurationFile> file.Text
        |> head
        |> function
        | Success s -> Some s.Data
        | DeserializeResult.Error e ->
            let message = e.Error.Head.Message

            logger
                .At(Error)
                .Log("Failed to parse redirect configuration: {message}", setField "message" message)

            None

    let findMatch (configs: RedirectConfigurationFile) (request: HttpRequest): string option =
        let checkMatch (found: string option) (redirect: RedirectConfiguration) =
            match found with
            | Some value -> Some value
            | None ->
                let hostMatch =
                    Regex.Match(request.host, redirect.Origin.Host)

                let pathMatch =
                    Regex.Match(request.path, redirect.Origin.Path)

                match (hostMatch.Success, pathMatch.Success) with
                | (true, true) ->
                    let matchedGroup = pathMatch.Groups.[0]

                    let rest =
                        request.path.Replace(matchedGroup.Value, "")

                    Some(redirect.Destination.Url + rest)
                | _ -> None

        fold checkMatch None configs.Redirects

    let buildRedirectPage (redirect: string) =
        "<html>"
        + "<head><noscript>"
        + "<meta http-equiv='refresh' content='0; url="
        + redirect
        + "'>"
        + "</noscript></head>"
        + "<body><script>"
        + "window.location.href = '"
        + redirect
        + "' + (window.location.search || '') + (window.location.hash || '');"
        + "</script></body>"
        + "</html>"

    let handle (logger: TotopoLogger) (fileReader: FileReader) (context: HttpContext): Async<HttpContext option> =
        async {
            try
                let redirectsConfig = fileReader (FilePath "/redirect.yaml")

                let configuration =
                    foldMap (parseConfiguration logger) redirectsConfig

                let matchRequest = foldMap findMatch configuration

                let result =
                    match matchRequest context.request with
                    | Some r ->
                        logger
                            .At(Info)
                            .Log(
                                "Redirecting {host}{path}",
                                setField "host" context.request.host
                                >> setField "path" context.request.path
                            )

                        let page = buildRedirectPage r
                        Successful.OK page context
                    | None -> Async.result None

                return! result
            with e ->
                logger
                    .At(Error)
                    .With(e)
                    .Log("Failed to handle redirect: {message}", setField "message" e.Message)

                return! Async.result None
        }
