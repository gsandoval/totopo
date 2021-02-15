# Copyright 2020 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

FROM mcr.microsoft.com/dotnet/sdk:5.0.102-ca-patch-buster-slim AS build

WORKDIR /stage
COPY Totopo Totopo

WORKDIR /app
RUN cp -r /stage/Totopo/* ./

RUN dotnet restore -r linux-x64
RUN dotnet publish -c release -o out -r linux-x64 --self-contained true --no-restore /p:PublishTrimmed=true /p:PublishReadyToRun=true

COPY scripts/docker/container-totopo.sh out/

FROM  mcr.microsoft.com/dotnet/runtime:5.0 AS runtime
RUN apt-get update && apt-get install -y libc-dev
WORKDIR /app

COPY --from=build /app/out ./
COPY resources/ ./resources

ENTRYPOINT ["/app/container-totopo.sh"]