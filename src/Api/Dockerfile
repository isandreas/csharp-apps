# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo

# Copy solution and project files first for layer caching
COPY global.json Directory.Build.props ./
COPY src/Api/Api.csproj src/Api/

# Restore packages (cached layer unless .csproj changes)
RUN dotnet restore src/Api/Api.csproj

# Copy full source and publish
COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Api.csproj \
      --configuration Release \
      --output /app/publish

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for health-check probes
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build --chown=appuser:appgroup /app/publish ./

USER appuser

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
