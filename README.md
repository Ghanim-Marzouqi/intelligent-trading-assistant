# Intelligent Trading Assistant

A trading assistant application for IC Markets via the cTrader Open API. Provides smart alerts, automated trade journaling, semi-automated trade execution with human approval, and AI-powered market analysis.

## Features

- **Real-time Price Monitoring** — Stream live prices via cTrader gRPC API
- **Smart Alerts** — Price levels, indicator conditions (RSI, MACD, Bollinger), composite rules
- **Automated Trade Journal** — Captures every trade with P&L, R:R ratio, duration, and costs
- **Performance Analytics** — Win rate, profit factor, equity curve, pair/time analysis
- **Semi-Automated Execution** — Telegram approval workflow for trade orders
- **Risk Management** — Position sizing, daily loss limits, max positions per symbol
- **AI Market Analysis** — Daily briefings, trade reviews, and news sentiment via OpenCode Zen
- **Telegram Bot** — Full command interface for account status, alerts, analysis, and order approval
- **Angular Dashboard** — Real-time positions, charts, alerts, journal, analytics, and AI analysis

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | ASP.NET Core 10 (.NET 10) |
| Frontend | Angular 19 |
| Database | PostgreSQL 18 |
| Cache | Redis 7 |
| Real-time | SignalR WebSocket |
| Broker API | cTrader Open API (gRPC/Protobuf) |
| Notifications | Telegram Bot + WhatsApp Cloud API |
| AI | OpenCode Zen API |
| Monitoring | Prometheus + Grafana |
| Reverse Proxy | Traefik v3.6 |
| Containerization | Docker + Compose |

## Quick Start

### Prerequisites

- Docker and Docker Compose v2+
- IC Markets cTrader account (demo or live)
- cTrader Open API credentials ([open.ctrader.com](https://open.ctrader.com))
- Telegram Bot token ([BotFather](https://t.me/botfather))

### Setup

1. Clone the repository:
   ```bash
   git clone git@github.com:Ghanim-Marzouqi/intelligent-trading-assistant.git
   cd intelligent-trading-assistant
   ```

2. Create environment file:
   ```bash
   cp .env.example .env
   ```

3. Configure `.env` with your credentials:
   ```env
   CTRADER_ENVIRONMENT=demo
   CTRADER_ACCOUNT_ID=12345678
   CTRADER_CLIENT_ID=your_client_id
   CTRADER_CLIENT_SECRET=your_client_secret

   TELEGRAM_BOT_TOKEN=your_bot_token
   TELEGRAM_CHAT_ID=your_chat_id

   OPENCODE_ZEN_API_KEY=your_api_key
   JWT_SECRET=your_jwt_secret_at_least_32_chars
   AUTH_PASSWORD=your_dashboard_password
   ```

4. Start the local stack:
   ```bash
   docker compose --profile local up --build -d
   ```

5. Access the services:

   | Service | URL |
   |---------|-----|
   | Dashboard | http://localhost:4200 |
   | API | http://localhost:5000 |
   | Swagger | http://localhost:5000/swagger |

6. Authorize cTrader: visit `http://localhost:5000/api/auth/ctrader` and complete the OAuth flow.

## Project Structure

```
├── docker-compose.yml              # Production service orchestration
├── docker-compose.override.yml     # Local dev overrides (profiles, ports)
├── src/
│   ├── TradingAssistant.Api/       # ASP.NET Core backend
│   │   ├── Controllers/            # 8 REST API controllers
│   │   ├── Hubs/                   # SignalR real-time hub
│   │   ├── Services/
│   │   │   ├── CTrader/            # Broker API integration (9 files)
│   │   │   ├── Alerts/             # Alert engine + conditions
│   │   │   ├── Journal/            # Trade journaling + analytics
│   │   │   ├── Orders/             # Order management + risk guards
│   │   │   ├── AI/                 # AI analysis service
│   │   │   ├── Analysis/           # Scheduled analysis runner
│   │   │   └── Notifications/      # Telegram, WhatsApp, SignalR
│   │   ├── Models/                 # Domain entities (Trading, Alerts, Journal, Analytics)
│   │   └── Data/                   # EF Core DbContext + migrations
│   └── trading-ui/                 # Angular frontend
│       └── src/app/
│           ├── dashboard/          # Real-time account overview
│           ├── positions/          # Position management + order placement
│           ├── alerts/             # Alert rule configuration
│           ├── journal/            # Trade journal browser
│           ├── analytics/          # Performance charts
│           ├── ai-analysis/        # AI market analysis + charts
│           ├── watchlist/          # Watchlist + scan settings
│           ├── auth/               # Login + auth guards
│           └── shared/             # Dialogs, notifications, interceptors
├── tests/
│   └── TradingAssistant.Tests/     # 78 unit tests
├── scripts/                        # Backup, deploy, DB init
└── docs/
    ├── ARCHITECTURE.md             # System design and decisions
    ├── LOCAL-DEVELOPMENT.md        # Docker Compose setup guide
    └── API.md                      # Complete endpoint reference
```

## Telegram Commands

| Command | Description |
|---------|-------------|
| `/status` | Account balance, equity, and margin |
| `/positions` | List open positions with P&L |
| `/alerts` | List active alert rules |
| `/alert EURUSD > 1.0900` | Create a quick one-shot price alert |
| `/today` | Today's trading summary |
| `/week` | This week's performance |
| `/analyze EURUSD` | AI market analysis |
| `/briefing` | Daily market briefing for watchlist |
| `/review 123` | AI trade review by ID |
| `/news EURUSD` | News sentiment analysis |
| `/help` | Show all commands |

## Development

See [docs/LOCAL-DEVELOPMENT.md](docs/LOCAL-DEVELOPMENT.md) for the full local development guide.

```bash
# Database + cache only
docker compose up postgres redis -d

# Full local stack
docker compose --profile local up --build -d

# Run backend outside Docker
cd src/TradingAssistant.Api && dotnet run

# Run frontend outside Docker
cd src/trading-ui && npm install --legacy-peer-deps && npx ng serve

# Run tests
dotnet test
```

## Documentation

| Document | Description |
|----------|-------------|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | System design, database schema, service architecture, security, deployment |
| [LOCAL-DEVELOPMENT.md](docs/LOCAL-DEVELOPMENT.md) | Docker profiles, ports, environment variables, database management |
| [API.md](docs/API.md) | Complete REST API and SignalR endpoint reference |

## Security

- JWT authentication with configurable expiry
- Rate limiting: global (100/min), auth (10/min), trading operations (10/min)
- Pagination limits clamped to prevent unbounded queries
- Explicit DB transactions on multi-step operations
- Telegram bot restricted to authorized chat ID only
- Secrets managed via environment variables (never committed)

## License

Private project. All rights reserved.

---

> **Disclaimer:** This system is a trading assistant tool, not a fully autonomous trading bot. All trade execution requires human approval. Trading forex and CFDs carries significant risk. Only trade with money you can afford to lose.
