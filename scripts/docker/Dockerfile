FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine AS build
WORKDIR /app

COPY Totopo/* ./
RUN dotnet restore
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine AS runtime
WORKDIR /app

COPY --from=build /app/out ./
COPY resources/ ./resources

ENTRYPOINT ["dotnet", "Totopo.dll"]