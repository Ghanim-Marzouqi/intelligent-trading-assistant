# Local Development Guide

## Prerequisites

- Docker and Docker Compose v2+
- .NET 10 SDK (for running outside Docker)
- Node.js 20+ (for Angular CLI outside Docker)

## Quick Start

```bash
# 1. Start database and cache
docker compose up postgres redis -d

# 2. Start the full local stack (API + UI + DB + Redis)
docker compose --profile local up --build -d
```

The services will be available at:

| Service | URL |
|---------|-----|
| Angular UI | http://localhost:4200 |
| API | http://localhost:5000 |
| API Health | http://localhost:5000/health |
| Swagger (dev) | http://localhost:5000/swagger |
| PostgreSQL | localhost:5432 |
| Redis | localhost:6379 |

## Docker Compose Profiles

The `docker-compose.override.yml` defines profiles to control which services start:

| Profile | Services | Usage |
|---------|----------|-------|
| *(none)* | postgres, redis | `docker compose up -d` |
| `local` | All of the above + trading-api + trading-ui | `docker compose --profile local up --build -d` |
| `observability` | Prometheus + Grafana | `docker compose --profile observability up -d` |
| `production` | Full production stack with Traefik | Not for local use |

Profiles can be combined:

```bash
# Full local stack + monitoring
docker compose --profile local --profile observability up --build -d
```

## Environment Variables

The override file provides sensible defaults for local development. To customise, create a `.env` file in the project root (it's in `.gitignore`). See `.env.example` for the template.

Key variables with their local defaults:

| Variable | Default | Purpose |
|----------|---------|---------|
| `CTRADER_ENVIRONMENT` | `demo` | cTrader API environment |
| `CTRADER_ACCOUNT_ID` | `0` | cTrader account ID |
| `CTRADER_CLIENT_ID` | *(empty)* | cTrader OAuth client ID |
| `CTRADER_CLIENT_SECRET` | *(empty)* | cTrader OAuth client secret |
| `TELEGRAM_BOT_TOKEN` | *(empty)* | Telegram bot token (optional locally) |
| `TELEGRAM_CHAT_ID` | `0` | Authorized Telegram chat ID |
| `OPENCODE_ZEN_API_KEY` | *(from override)* | AI analysis API key |
| `JWT_SECRET` | Auto-generated dev key | JWT signing secret |
| `AUTH_PASSWORD` | `admin` | Dashboard login password |

## Running Without Docker

### Backend

```bash
cd src/TradingAssistant.Api

# Ensure PostgreSQL and Redis are running (via Docker or locally)
docker compose up postgres redis -d

# Run the API
dotnet run
```

The API starts on `http://localhost:5000` (or the port configured in `launchSettings.json`).

### Frontend

```bash
cd src/trading-ui
npm install --legacy-peer-deps
npx ng serve
```

The UI starts on `http://localhost:4200` and proxies API calls to `http://localhost:5000`.

### Tests

```bash
# From the repository root
dotnet test

# With verbose output
dotnet test --verbosity normal
```

## Database

### Migrations

EF Core migrations run automatically in Development mode. To run manually:

```bash
cd src/TradingAssistant.Api
dotnet ef database update
```

### Creating a new migration

```bash
cd src/TradingAssistant.Api
dotnet ef migrations add MigrationName
```

### Connecting directly

```bash
docker exec -it intelligent-trading-assistant-postgres-1 \
  psql -U trading_user -d trading_assistant
```

## Ports Reference

| Port | Service | Exposed In |
|------|---------|------------|
| 4200 | Angular UI (nginx) | local, production |
| 5000 | ASP.NET Core API | local |
| 5432 | PostgreSQL | local (override) |
| 6379 | Redis | local (override) |
| 9090 | Prometheus | observability |
| 3000 | Grafana | observability |

## Logs

```bash
# All services
docker compose --profile local logs -f

# Specific service
docker compose --profile local logs -f trading-api

# Last 100 lines
docker compose --profile local logs --tail 100 trading-api
```

## Rebuilding

```bash
# Rebuild and restart a single service
docker compose --profile local up --build -d trading-api

# Full rebuild (no cache)
docker compose --profile local build --no-cache
docker compose --profile local up -d
```

## Stopping

```bash
# Stop all services
docker compose --profile local down

# Stop and remove volumes (resets database)
docker compose --profile local down -v
```
