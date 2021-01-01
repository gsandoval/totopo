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

namespace Totopo.Templates

open FSharpPlus.Operators
open System
open Totopo.Configuration

module ViewModels =
    type ViewTime(time: DateTime) =
        member this.Year = time.Year
        member this.Month = time.Month
        member this.Day = time.Day
        member this.Hour = time.Hour
        member this.Minute = time.Minute
        member this.Second = time.Second
        member this.Millisecond = time.Millisecond

        member this.Human =
            $"{this.Year}-{this.Month:D2}-{this.Day:D2} {this.Hour:D2}:{this.Minute:D2} UTC"

        member this.Iso8601 =
            $"{this.Year}-{this.Month:D2}-{this.Day:D2}T{this.Hour:D2}:{this.Minute:D2}:{this.Second:D2}.{this.Millisecond:D3}Z"

    type ViewTemplate(template: Template, allTemplates: Template list) =
        member this.Directory =
            TemplatePath.value (TemplatePath.parent template.Path)

        member this.ParentDirectory =
            TemplatePath.value (TemplatePath.parent (TemplatePath.parent template.Path))

        member this.UpdatedAt = ViewTime(template.Contents.UpdatedAt)

        member this.PathUpdatedAt =
            let isInPath (template: Template) =
                (TemplatePath.value template.Path).IndexOf(this.Directory) = 0
            let allInPath = filter isInPath allTemplates
            let getMostRecentTime (mostRecent: DateTime) (ct: Template) =
                match mostRecent > ct.Contents.UpdatedAt with
                | true -> mostRecent
                | false -> ct.Contents.UpdatedAt
            let mostRecentTime = fold getMostRecentTime DateTime.UnixEpoch allInPath
            ViewTime(mostRecentTime)

    type ViewTemplates(template: LoadedTemplate) =
        let getTemplatesMatchingPath (pathStr: string): Template list =
            let inPath (template: Template): bool =
                let templatePathStr = TemplatePath.value template.Path
                templatePathStr.IndexOf(pathStr) = 0

            filter inPath template.ReferencedTemplates

        let getMostRecentlyUpdatedTemplate (templates: Template list): Template option =
            match templates with
            | [] -> None
            | _ ->

                head templates |> Some

        let allTemplates =
            template.Template :: template.ReferencedTemplates

        member this.Main =
            ViewTemplate(template.Template, allTemplates)

        member this.TemplatesById =
            map (fun t -> (TemplateId.value t.Id, ViewTemplate(t, allTemplates) :> obj)) allTemplates
            |> dict

        member this.GetMostRecentUpdateTime =
            Func<string, obj>(
                (fun path ->
                    let matchingTemplates = getTemplatesMatchingPath path

                    let mostRecentTemplate =
                        getMostRecentlyUpdatedTemplate matchingTemplates

                    let time =
                        match mostRecentTemplate with
                        | Some template -> ViewTime(template.Contents.UpdatedAt)
                        | None -> ViewTime(DateTime.UtcNow)

                    time :> obj)
            )

    type ViewServer() =
        member this.Time = ViewTime(DateTime.UtcNow)

    type ViewMain(cdnBase: CdnBaseUrl, template: LoadedTemplate) =
        member this.StaticResource =
            Func<string, obj>((fun x -> CdnBaseUrl.value cdnBase + x :> obj))

        member this.Templates = ViewTemplates(template)

        member this.Server = ViewServer()
