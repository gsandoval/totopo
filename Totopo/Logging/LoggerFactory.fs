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

namespace Totopo.Logging

open Suave.Logging

type LoggerFactory(logName: string, minLogLevel: LogLevel, cloudSettings: CloudLoggingSettings, logToConsole: bool) =
    let createRootLogger (rootLoggerName: string) (minLevel: LogLevel) =
        let name = [| rootLoggerName |]

        let cloudLogger =
            [ GoogleCloudLoggerSuaveAdapter(name, minLevel, cloudSettings) :> Logger ]

        let consoleLogger =
            match logToConsole with
            | true -> [ LiterateConsoleTarget(name, minLevel) :> Logger ]
            | _ -> []

        let logger =
            CombiningTarget(name, [ cloudLogger; consoleLogger ] |> List.concat) :> Logger

        logger

    let suaveLogger = createRootLogger logName minLogLevel

    member this.Create() = TotopoLogger(suaveLogger)

    member this.SuaveLogger = suaveLogger
