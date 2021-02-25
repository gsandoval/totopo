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
    let error = "<exception>"

// generic_task
// Generic Task	A generic task identifies an application process for which no more specific resource is applicable, such as a process scheduled by a custom orchestration system. The label values must uniquely identify the task.
// 
// project_id: The identifier of the GCP project associated with this resource, such as "my-project".
// location: The GCP or AWS region in which data about the resource is stored. For example, "us-east1-a" (GCP) or "aws:us-east-1a" (AWS).
// namespace: A namespace identifier, such as a cluster name.
// job: An identifier for a grouping of related tasks, such as the name of a microservice or distributed batch job.
// task_id: A unique identifier for the task within the namespace and job, such as a replica index identifying the task within the job.

type LoggingContext =
    { Logger: Logger
      Severity: LogLevel
      FileName: string
      FileLine: int
      FunctionName: string
      Error: Exception option }
    member this.With(error: Exception) =
        { this with Error = Some error }

    member private this.BuildEvent textPayload =
        let event = eventX textPayload
                        >> setField ExtraFields.fileName this.FileName
                        >> setField ExtraFields.fileLine this.FileLine
                        >> setField ExtraFields.functionName this.FunctionName
        match this.Error with
        | Some err -> event >> setField ExtraFields.error err
        | None -> event

    member this.Log(textPayload: string, messageModifier: Message -> Message) =
        let event = this.BuildEvent textPayload
        this.Logger.log this.Severity (event >> messageModifier)

    member this.Log(textPayload: string) =
        let event = this.BuildEvent textPayload
        this.Logger.log this.Severity event

type TotopoLogger(logger: Logger) =
    member this.At
        (
            level: LogLevel,
            [<CallerMemberName; Optional; DefaultParameterValue("")>] memberName: string,
            [<CallerFilePath; Optional; DefaultParameterValue("")>] path: string,
            [<CallerLineNumber; Optional; DefaultParameterValue(0)>] line: int
        ): LoggingContext =
        { Logger = logger
          Severity = level
          FileName = path
          FileLine = line
          FunctionName = memberName
          Error = None }

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
        let textPayload =
            match msg.fields.TryFind ExtraFields.error with
            | Some err -> textPayload + "\n" + err.ToString()
            | None -> textPayload

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
            cloudClient.WriteLogEntries(writeRequest)
            |> ignore

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
