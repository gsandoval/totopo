FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build

WORKDIR /stage
COPY Totopo Totopo

WORKDIR /app
RUN cp -r /stage/Totopo/* ./

RUN dotnet restore -r linux-x64
RUN dotnet publish -c release -o out -r linux-x64 --self-contained true --no-restore /p:PublishTrimmed=true /p:PublishReadyToRun=true

COPY scripts/docker/container-totopo.sh out/

FROM  mcr.microsoft.com/dotnet/runtime:5.0 AS runtime
WORKDIR /app

COPY --from=build /app/out ./
COPY resources/ ./resources

ENTRYPOINT ["/app/container-totopo.sh"]