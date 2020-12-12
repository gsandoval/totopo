FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build

WORKDIR /stage
COPY Totopo Totopo

WORKDIR /app
RUN cp -r /stage/Totopo/* ./

RUN dotnet restore
RUN dotnet publish -c Release -o out

COPY scripts/docker/container-totopo.sh out/

FROM mcr.microsoft.com/dotnet/runtime:3.1 AS runtime
WORKDIR /app

COPY --from=build /app/out ./
COPY resources/ ./resources

ENTRYPOINT ["/app/container-totopo.sh"]