# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props .editorconfig ./
COPY backend/src/SharedKernel/ src/SharedKernel/
COPY backend/src/Modules/Ai/Ai.Contracts/ src/Modules/Ai/Ai.Contracts/
COPY backend/src/Host.Mcp/ src/Host.Mcp/
RUN dotnet restore src/Host.Mcp/Host.Mcp.csproj
RUN dotnet publish src/Host.Mcp/Host.Mcp.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# No database driver, no connection string, no finance code: the MCP server can only call the AI gateway.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Host.Mcp.dll"]
