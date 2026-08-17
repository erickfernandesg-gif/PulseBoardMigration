FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PulseBoardMigration.csproj ./
RUN dotnet restore PulseBoardMigration.csproj

COPY . ./
RUN dotnet publish PulseBoardMigration.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:10000 \
    ASPNETCORE_ENVIRONMENT=Staging \
    DOTNET_EnableDiagnostics=0

EXPOSE 10000
COPY --from=build /app/publish ./

USER $APP_UID
ENTRYPOINT ["dotnet", "PulseBoardMigration.dll"]
