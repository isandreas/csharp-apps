# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo

COPY global.json Directory.Build.props ./
COPY src/WebSocketService/WebSocketService.csproj src/WebSocketService/

RUN dotnet restore src/WebSocketService/WebSocketService.csproj

COPY src/WebSocketService/ src/WebSocketService/
RUN dotnet publish src/WebSocketService/WebSocketService.csproj \
      --configuration Release \
      --no-restore \
      --output /app/publish

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build --chown=appuser:appgroup /app/publish ./

USER appuser

ENV ASPNETCORE_URLS=http://+:8081 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8081

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:8081/healthz || exit 1

ENTRYPOINT ["dotnet", "WebSocketService.dll"]
