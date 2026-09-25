# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Host.Api/Host.Api.csproj
RUN dotnet publish src/Host.Api/Host.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false \
    && mkdir -p /keys

# Chiseled: no shell, no package manager, runs as non-root "app" (uid 1654) by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
# Mount point for the Data Protection key ring; a fresh named volume inherits this ownership.
COPY --from=build --chown=1654:1654 /keys /keys
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["dotnet", "Host.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Host.Api.dll"]
