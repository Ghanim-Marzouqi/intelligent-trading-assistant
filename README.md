# Intelligent Trading Assistant

A trading assistant application for IC Markets via the cTrader Open API. Provides smart alerts, automated trade journaling, and semi-automated trade execution with human approval.

## Features

- **Real-time Price Monitoring** — Stream live prices via cTrader gRPC API
- **Smart Alerts** — Price levels, indicator conditions (RSI, MACD, Bollinger), composite rules
- **Automated Trade Journal** — Captures every trade with P&L, R:R ratio, duration, and costs
- **Performance Analytics** — Win rate, profit factor, equity curve, pair/time analysis
- **Semi-Automated Execution** — Telegram approval workflow for trade orders
- **Risk Management** — Position sizing, daily loss limits, correlation checks
- **AI Analysis (Phase 4)** — Market briefings and trade reviews via OpenCode Zen

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | ASP.NET Core 10 (.NET 10 LTS) |
| Frontend | Angular 19 |
| Database | PostgreSQL 18 |
| Real-time | SignalR WebSocket |
| Broker API | cTrader Open API (gRPC/Protobuf) |
| Notifications | Telegram Bot + WhatsApp Cloud API |
| AI | OpenCode Zen API (GLM 4.7) |
| Reverse Proxy | Traefik v3.6 |
| Containerization | Docker + Compose |

## Quick Start

### Prerequisites

- Docker & Docker Compose
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
   DB_USER=trading_user
   DB_PASSWORD=your_secure_password

   CTRADER_ENVIRONMENT=demo
   CTRADER_ACCOUNT_ID=12345678
   CTRADER_CLIENT_ID=your_client_id
   CTRADER_CLIENT_SECRET=your_client_secret

   TELEGRAM_BOT_TOKEN=your_bot_token
   TELEGRAM_CHAT_ID=your_chat_id
   ```

4. Start the stack:
   ```bash
   docker compose up -d
   ```

5. Access the dashboard at `http://localhost` (or your configured domain)

## Project Structure

```
├── docker-compose.yml          # Service orchestration
├── traefik/                    # Reverse proxy config
├── scripts/                    # Backup and deployment scripts
├── src/
│   ├── TradingAssistant.Api/   # ASP.NET Core backend
│   │   ├── Controllers/        # REST API endpoints
│   │   ├── Hubs/               # SignalR hub
│   │   ├── Services/           # Business logic
│   │   │   ├── CTrader/        # Broker API integration
│   │   │   ├── Alerts/         # Alert engine
│   │   │   ├── Journal/        # Trade journaling
│   │   │   ├── Orders/         # Order management
│   │   │   ├── AI/             # AI analysis
│   │   │   └── Notifications/  # Telegram, WhatsApp
│   │   ├── Models/             # Domain entities
│   │   └── Data/               # EF Core DbContext
│   └── trading-ui/             # Angular frontend
│       └── src/app/
│           ├── dashboard/      # Overview
│           ├── positions/      # Position management
│           ├── alerts/         # Alert configuration
│           ├── journal/        # Trade journal
│           └── analytics/      # Performance charts
└── docs/
    └── ARCHITECTURE.md         # Detailed system design
```

## Telegram Commands

| Command | Description |
|---------|-------------|
| `/status` | Account balance, equity, and margin |
| `/positions` | List open positions |
| `/alerts` | List active alert rules |
| `/alert EURUSD > 1.0900` | Create a quick price alert |
| `/today` | Today's trading summary |
| `/week` | This week's performance |
| `/calendar` | Upcoming economic events |

## Development

### Backend

```bash
cd src/TradingAssistant.Api
dotnet restore
dotnet run
```

### Frontend

```bash
cd src/trading-ui
npm install
npm start
```

### Database Migrations

```bash
cd src/TradingAssistant.Api
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed system design, including:

- Database schema design
- Service layer architecture
- cTrader API integration
- Alert engine implementation
- Observability recommendations

## Roadmap

- [x] Phase 1: Foundation + Smart Alerts
- [ ] Phase 2: Trade Journal + Analytics
- [ ] Phase 3: Semi-Automated Execution
- [ ] Phase 4: AI Integration + News Monitoring

## License

Private project. All rights reserved.

---

> **Disclaimer:** This system is a trading assistant tool, not a fully autonomous trading bot. All trade execution requires human approval. Trading forex and CFDs carries significant risk. Only trade with money you can afford to lose.
