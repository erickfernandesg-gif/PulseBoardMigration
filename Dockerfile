FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY PulseBoardMigration.csproj ./
RUN dotnet restore PulseBoardMigration.csproj

COPY . ./
RUN dotnet publish PulseBoardMigration.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:10000 \
    ASPNETCORE_ENVIRONMENT=Staging \
    DOTNET_EnableDiagnostics=0

EXPOSE 10000
COPY --from=build /app/publish ./
RUN groupadd --gid 1654 app \
    && useradd --uid 1654 --gid app --no-create-home app \
    && mkdir -p /app/App_Data/keys \
    && chown -R app:app /app/App_Data

USER 1654:1654
ENTRYPOINT ["dotnet", "PulseBoardMigration.dll"]
