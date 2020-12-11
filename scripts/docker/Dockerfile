FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine AS build

WORKDIR /stage
COPY Totopo Totopo

WORKDIR /app
RUN cp -r /stage/Totopo/* ./

RUN dotnet restore
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine AS runtime
WORKDIR /app

COPY --from=build /app/out ./
COPY resources/ ./resources

ENTRYPOINT ["dotnet", "Totopo.dll", "--http-port", "50001", "--totopo-resources-path", "/app/resources/totopo", "--application-resources-path", "/app/resources/hackergs"]