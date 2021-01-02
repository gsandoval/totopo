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

open FSharpPlus.Operators
open Google.Api
open Google.Cloud.Logging.V2
open Google.Cloud.Logging.Type
open Suave.Logging
open Suave.Logging.Message
open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Text.RegularExpressions

type internal ParsedField =
    { Key: string
      Format: string
      Original: string }

type FallbackMode =
    | Disabled
    | Console

type CloudLoggingSettings =
    { ProjectName: string
      CloudClient: LoggingServiceV2Client option
      MonitoredResource: MonitoredResource
      FallbackMode: FallbackMode }

[<AutoOpen>]
type LogLevel = Suave.Logging.LogLevel

[<AutoOpen>]
module MessageProducerHelpers =
    let setField = Message.setField

module ExtraFields =
    let fileName = "<file_name>"
    let fileLine = "<file_line>"
    let functionName = "<function_name>"

type TotopoLogger(logger: Logger) =
    member this.Log
        (
            level: LogLevel,
            textPayload: string,
            messageModifier: Message -> Message,
            [<CallerMemberName; Optional; DefaultParameterValue("")>] memberName: string,
            [<CallerFilePath; Optional; DefaultParameterValue("")>] path: string,
            [<CallerLineNumber; Optional; DefaultParameterValue(0)>] line: int
        ) =
        logger.log
            level
            (eventX textPayload
             >> setField ExtraFields.fileName path
             >> setField ExtraFields.fileLine line
             >> setField ExtraFields.functionName memberName
             >> messageModifier)

    member this.Log
        (
            level: LogLevel,
            textPayload: string,
            [<CallerMemberName; Optional; DefaultParameterValue("")>] memberName: string,
            [<CallerFilePath; Optional; DefaultParameterValue("")>] path: string,
            [<CallerLineNumber; Optional; DefaultParameterValue(0)>] line: int
        ) =
        logger.log
            level
            (eventX textPayload
             >> setField ExtraFields.fileName path
             >> setField ExtraFields.fileLine line
             >> setField ExtraFields.functionName memberName)

type GoogleCloudLoggerSuaveAdapter(name: string array, minLevel: LogLevel, cloudSettings: CloudLoggingSettings) =
    let log (msg: Suave.Logging.Message) =
        let convertToCloudLevel (level: LogLevel) =
            match level with
            | Verbose -> LogSeverity.Notice
            | Debug -> LogSeverity.Debug
            | Info -> LogSeverity.Info
            | Warn -> LogSeverity.Warning
            | Error -> LogSeverity.Error
            | Fatal -> LogSeverity.Critical

        let formatMessage (msg: Suave.Logging.Message): string =
            let textPayload =
                match msg.value with
                | Event (str) -> str
                | Gauge (value, units) -> $"{value}" + units

            let textFieldRegex = Regex @"\{([^\}]+)\}"
            let matchedTextFields = textFieldRegex.Matches textPayload

            let parseField (matched: MatchCollection) index: ParsedField =
                let item = matched.Item index
                let captureGroup = item.Groups.Item 1

                match captureGroup.Value.IndexOf ":" with
                | -1 ->
                    { Key = captureGroup.Value
                      Format = "{0}"
                      Original = "{" + captureGroup.Value + "}" }
                | ind ->
                    { Key = captureGroup.Value.Substring(0, ind)
                      Format =
                          "{0:"
                          + captureGroup.Value.Substring(ind + 1)
                          + "}"
                      Original = "{" + captureGroup.Value + "}" }

            let foundFields =
                [ 0 .. matchedTextFields.Count - 1 ]
                |> map (parseField matchedTextFields)

            let replaceFields (state: string) (field: ParsedField) =
                let fieldValue = msg.fields.TryFind field.Key

                match fieldValue with
                | Some value ->
                    let formattedValue = String.Format(field.Format, value)
                    state.Replace(field.Original, formattedValue)
                | None -> state.Replace(field.Original, "{missing:" + field.Key + "}")

            fold replaceFields textPayload foundFields

        let cloudLogName =
            LogName(cloudSettings.ProjectName, String.concat "." name)

        let severity = convertToCloudLevel msg.level
        let textPayload = formatMessage msg

        let fileName =
            Option.defaultValue ("" :> obj) (msg.fields.TryFind ExtraFields.fileName)

        let fileLine =
            Option.defaultValue (0 :> obj) (msg.fields.TryFind ExtraFields.fileLine)

        let functionName =
            Option.defaultValue ("" :> obj) (msg.fields.TryFind ExtraFields.functionName)

        let sourceLocation =
            LogEntrySourceLocation(
                File = fileName.ToString(),
                Line = int64 (fileLine.ToString()),
                Function = functionName.ToString()
            )

        let logEntry =
            LogEntry(
                LogName = cloudLogName.ToString(),
                Severity = severity,
                TextPayload = textPayload,
                SourceLocation = sourceLocation
            )

        let writeRequest =
            WriteLogEntriesRequest(LogName = logEntry.LogName, Resource = cloudSettings.MonitoredResource)

        writeRequest.Entries.Add logEntry

        match cloudSettings.CloudClient with
        | Some cloudClient ->
            cloudClient.WriteLogEntries(writeRequest) |> ignore
            None
        | None ->
            match cloudSettings.FallbackMode with
            | Console ->
                System.Console.WriteLine(textPayload)
                None
            | Disabled -> None
        |> ignore
        

    interface Logger with
        member x.name = name

        member x.log level messageFactory =
            if level >= minLevel then
                log (messageFactory level)

        member x.logWithAck level messageFactory =
            if level >= minLevel then
                log (messageFactory level)

            async.Return()
