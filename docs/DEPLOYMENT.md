# Deployment Runbook

## Prerequisites

| Requirement | Minimum Version |
|---|---|
| Docker | 24.x |
| Docker Compose plugin | v2.x (`docker compose` not `docker-compose`) |
| Linux VM | Ubuntu 22.04 LTS or equivalent |
| Open ports | TCP 8080 (API), TCP 8081 (WebSocket), TCP 443 (NGINX TLS) |

---

## 1. Clone and Configure

```bash
git clone <repo-url> csharp-apps
cd csharp-apps

# Copy the example env file and fill in ALL secrets
cp infra/.env.example infra/.env
nano infra/.env          # Set API_KEY, WS_API_KEY, POSTGRES_PASSWORD
```

> **Security**: `infra/.env` is in `.gitignore`. Never commit it.

---

## 2. First-Time Deploy

```bash
# Build images and start all services in detached mode
docker compose -f infra/docker-compose.yml --env-file infra/.env up -d --build
```

Wait ~30 seconds for Postgres to become healthy, then verify:

```bash
docker compose -f infra/docker-compose.yml ps
```

---

## 3. Run EF Core Migrations

On the **first deploy** (or after schema changes), run migrations inside the running API container:

```bash
docker compose -f infra/docker-compose.yml exec api \
  dotnet ef database update \
  --project /app/Api.dll \
  --connection "Host=postgres;Port=5432;Database=appdb;Username=appuser;Password=<your-password>"
```

> **Tip**: In production it is safer to generate a migration SQL script and apply it manually:
> ```bash
> dotnet ef migrations script --idempotent --output migrations.sql
> psql -h <host> -U appuser -d appdb -f migrations.sql
> ```

---

## 4. Verify Health Checks

```bash
# REST API
curl -f http://localhost:8080/healthz && echo "API OK"

# WebSocket service
curl -f http://localhost:8081/healthz && echo "WebSocket OK"

# Authenticated endpoint
curl -H "X-Api-Key: <your-API_KEY>" http://localhost:8080/api/v1/items
```

---

## 5. View Logs

```bash
# Follow all services
docker compose -f infra/docker-compose.yml logs -f

# Individual service
docker compose -f infra/docker-compose.yml logs -f api
docker compose -f infra/docker-compose.yml logs -f websocket
docker compose -f infra/docker-compose.yml logs -f postgres
```

---

## 6. Rolling Update / Redeploy

```bash
git pull origin main

# Rebuild only changed services
docker compose -f infra/docker-compose.yml --env-file infra/.env up -d --build api
docker compose -f infra/docker-compose.yml --env-file infra/.env up -d --build websocket
```

---

## 7. Rollback

```bash
# Tag images on deploy so you can roll back
docker tag csharp-apps-api:latest csharp-apps-api:$(date +%Y%m%d%H%M)

# To roll back, re-tag and restart
docker tag csharp-apps-api:20240101120000 csharp-apps-api:latest
docker compose -f infra/docker-compose.yml --env-file infra/.env up -d api
```

---

## 8. Nginx Reverse Proxy (TLS Termination)

Install Nginx and Certbot on the VM, then use this configuration:

```nginx
# /etc/nginx/sites-available/csharp-apps

# Redirect HTTP → HTTPS
server {
    listen 80;
    server_name example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name example.com;

    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_ciphers         HIGH:!aNULL:!MD5;

    # ── REST API  →  api:8080 ───────────────────────────────
    location /api/ {
        proxy_pass         http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }

    # ── Health checks (no auth, no rewrite) ─────────────────
    location = /healthz {
        proxy_pass http://127.0.0.1:8080/healthz;
    }

    # ── WebSocket service  →  websocket:8081 ────────────────
    location /ws {
        proxy_pass             http://127.0.0.1:8081/ws;
        proxy_http_version     1.1;
        proxy_set_header       Upgrade    $http_upgrade;
        proxy_set_header       Connection "Upgrade";
        proxy_set_header       Host       $host;
        proxy_set_header       X-Real-IP  $remote_addr;
        proxy_read_timeout     3600s;   # Keep WS connections alive
        proxy_send_timeout     3600s;
    }
}
```

Enable and reload:

```bash
ln -s /etc/nginx/sites-available/csharp-apps /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx

# Obtain TLS certificate
certbot --nginx -d example.com
```

---

## 9. Environment Variables Reference

| Variable | Service | Description |
|---|---|---|
| `API_KEY` | api | Secret for `X-Api-Key` header auth |
| `WS_API_KEY` | websocket | Secret for WebSocket `?apiKey=` or `X-Api-Key` header |
| `ConnectionStrings__DefaultConnection` | api | PostgreSQL connection string |
| `POSTGRES_USER` | postgres | DB username |
| `POSTGRES_PASSWORD` | postgres | DB password |
| `POSTGRES_DB` | postgres | Database name |
| `ASPNETCORE_ENVIRONMENT` | both | `Production` or `Development` |

---

## 10. Useful Commands

```bash
# Stop everything
docker compose -f infra/docker-compose.yml down

# Stop and remove volumes (DESTRUCTIVE — deletes DB data)
docker compose -f infra/docker-compose.yml down -v

# Shell into running API container
docker compose -f infra/docker-compose.yml exec api /bin/sh

# Check resource usage
docker stats
```
