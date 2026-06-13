# CsharpApps — Production C# Monorepo

A production-ready .NET 8 monorepo running on Linux that contains two services:

| Service              | Description                                                        | Default port                   |
| -------------------- | ------------------------------------------------------------------ | ------------------------------ |
| **Api**              | REST API with CRUD endpoints, PostgreSQL persistence, API key auth | `5000` (dev) / `8080` (Docker) |
| **WebSocketService** | WebSocket server with API key handshake auth                       | `5001` (dev) / `8081` (Docker) |

---

## Prerequisites

| Tool                  | Version | Notes                                                                      |
| --------------------- | ------- | -------------------------------------------------------------------------- |
| .NET SDK              | 8.0     | Install from [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Docker                | 24+     | Required to run PostgreSQL locally                                         |
| Docker Compose plugin | v2      | `docker compose` (not `docker-compose`)                                    |

> **macOS note:** If `dotnet` is not on `$PATH`, locate it with `ls ~/.dotnet/dotnet` and prefix all `dotnet` commands with `~/.dotnet/` or add `~/.dotnet` to your `PATH`.

---

## Repository Layout

```
csharp-apps/
├── src/
│   ├── Api/                  # REST API service
│   └── WebSocketService/     # WebSocket service
├── tests/
│   ├── Api.Tests/
│   └── WebSocketService.Tests/
├── infra/
│   ├── docker-compose.yml          # Production compose
│   ├── docker-compose.override.yml # Dev overrides (auto-loaded locally)
│   └── .env.example                # Secret template
├── docs/
│   └── DEPLOYMENT.md         # Production Linux runbook
├── Directory.Build.props     # Shared .NET settings
├── global.json               # Pins .NET SDK version
└── CsharpApps.sln
```

---

## Run Locally (without Docker)

### 1 — Start PostgreSQL

```bash
docker run -d --name csharpapps-postgres \
  -e POSTGRES_USER=appuser \
  -e POSTGRES_PASSWORD=devpassword \
  -e POSTGRES_DB=appdb \
  -p 5432:5432 \
  postgres:16-alpine

# Wait until healthy
until [ "$(docker inspect --format='{{.State.Health.Status}}' csharpapps-postgres)" = "healthy" ]; do
  sleep 1
done && echo "Postgres ready"
```

### 2 — Apply database migrations

The app auto-migrates on startup in Development mode. If migrations do not apply automatically (e.g. EF tools mismatch), run them manually:

```bash
# Install EF Core CLI if not present
dotnet tool install --global dotnet-ef

# Ensure dotnet and dotnet-ef are on PATH (macOS ~/.dotnet install)
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

cd src/Api
DOTNET_ROOT="$HOME/.dotnet" \
  ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=devpassword" \
  dotnet-ef database update
```

> **Workaround if migration says "Done." but table is missing:** apply schema directly:
>
> ```bash
> docker exec csharpapps-postgres psql -U appuser -d appdb \
>   -c 'CREATE TABLE IF NOT EXISTS "Items" ("Id" uuid NOT NULL, "Name" character varying(256) NOT NULL, "Description" character varying(2000), "CreatedAt" timestamp with time zone NOT NULL, "UpdatedAt" timestamp with time zone NOT NULL, CONSTRAINT "PK_Items" PRIMARY KEY ("Id"));' \
>   -c 'CREATE INDEX IF NOT EXISTS ix_items_created_at ON "Items" ("CreatedAt");' \
>   -c 'CREATE INDEX IF NOT EXISTS ix_items_name ON "Items" ("Name");' \
>   -c "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\",\"ProductVersion\") VALUES ('20240101000000_InitialCreate','8.0.10') ON CONFLICT DO NOTHING;"
> ```

### 3 — Start the REST API

```bash
cd src/Api
ASPNETCORE_ENVIRONMENT=Development \
  ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=appdb;Username=appuser;Password=devpassword" \
  dotnet run
```

The API listens on **http://localhost:5000** by default.

### 4 — Start the WebSocket service

```bash
cd src/WebSocketService
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5001" dotnet run
```

The WebSocket service listens on **http://localhost:5001** by default.

---

## Verify the REST API

### Health check (no auth required)

```bash
curl http://localhost:5000/healthz
# → Healthy
```

### Swagger UI (dev only)

Open **http://localhost:5000/swagger** in a browser.  
Click **Authorize**, enter the API key `dev-api-key-change-me`.

### CRUD endpoints

All `/api/v1/items` endpoints require the `X-Api-Key` header.

```bash
KEY="dev-api-key-change-me"
API="http://localhost:5000"

# Create
curl -s -X POST "$API/api/v1/items" \
  -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -d '{"name":"My Item","description":"Optional description"}'

# List (paginated)
curl -s "$API/api/v1/items?page=1&pageSize=20" -H "X-Api-Key: $KEY"

# Get by ID
curl -s "$API/api/v1/items/<id>" -H "X-Api-Key: $KEY"

# Update
curl -s -X PUT "$API/api/v1/items/<id>" \
  -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -d '{"name":"Updated Name","description":"Updated"}'

# Delete
curl -s -o /dev/null -w "%{http_code}" -X DELETE "$API/api/v1/items/<id>" \
  -H "X-Api-Key: $KEY"
# → 204
```

### Auth rejection test

```bash
curl -s http://localhost:5000/api/v1/items
# → {"status":401,"title":"Unauthorized",...}
```

---

## Verify the WebSocket service

### Health check

```bash
curl http://localhost:5001/healthz
# → Healthy
```

### Connect (requires API key as query param or header)

```bash
# Using wscat (npm install -g wscat)
wscat -c "ws://localhost:5001/ws?apiKey=dev-ws-key-change-me"
```

---

## Run Tests

```bash
# From repo root
dotnet test
```

Coverage reports are written to `tests/**/coverage/`.

---

## Run with Docker Compose (production-like)

```bash
# Copy and populate the env file
cp infra/.env.example infra/.env
# Edit infra/.env — set API_KEY, WS_API_KEY, POSTGRES_PASSWORD, etc.

# Build images and bring up all services
docker compose -f infra/docker-compose.yml --env-file infra/.env up -d --build

# Check health
docker compose -f infra/docker-compose.yml ps
curl http://localhost:8080/healthz
curl http://localhost:8081/healthz
```

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the full Linux production runbook including TLS, rollback, and backup procedures.

---

## Environment Variables

| Variable                               | Service          | Description                                          | Dev default             |
| -------------------------------------- | ---------------- | ---------------------------------------------------- | ----------------------- |
| `ASPNETCORE_ENVIRONMENT`               | both             | `Development` / `Production`                         | `Development`           |
| `API_KEY`                              | Api              | Value clients must send in `X-Api-Key` header        | `dev-api-key-change-me` |
| `ConnectionStrings__DefaultConnection` | Api              | PostgreSQL connection string                         | see `appsettings.json`  |
| `WS_API_KEY`                           | WebSocketService | Value clients must send as `?apiKey=` or `X-Api-Key` | `dev-ws-key-change-me`  |

Secrets are **never** committed to source control. For production, inject them via `infra/.env` (Docker Compose) or platform-native secret management.

---

## Architecture notes

- **Auth:** API key middleware applied globally; `/healthz` and `/swagger` are exempt.
- **Persistence:** EF Core 8 + Npgsql; auto-migrate on startup in non-production environments.
- **Logging:** Serilog — plain text in Development, JSON in Production.
- **Observability:** correlation ID propagated in every request/response via `X-Correlation-ID` header.
- **Errors:** RFC 7807 problem details on all error responses.
- **Containers:** Multi-stage Dockerfiles; runtime image runs as non-root user.
