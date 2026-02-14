# Intelligent Trading Assistant — System Architecture

## IC Markets cTrader Integration | Angular + ASP.NET Core + PostgreSQL

**Version:** 1.2
**Date:** February 2026

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [System Overview](#system-overview)
3. [Technology Stack](#technology-stack)
4. [Database Design](#database-design)
5. [Core Services](#core-services)
6. [Docker Deployment](#docker-deployment)
7. [Security Considerations](#security-considerations)
8. [Development Phases](#development-phases)
9. [Testing Strategy](#testing-strategy)
10. [Telegram Bot Commands](#telegram-bot-commands)
11. [Estimated Monthly Costs](#estimated-monthly-costs)
12. [Next Steps](#next-steps)
13. [Architecture Recommendations](#architecture-recommendations)

---

## Executive Summary

This document outlines the system architecture for an Intelligent Trading Assistant application designed to work with IC Markets via the cTrader Open API. The application aims to eliminate the need for manual chart monitoring by providing smart alerts, automated trade journaling, and semi-automated trade execution capabilities.

The system is built on a proven technology stack (Angular, ASP.NET Core, PostgreSQL) deployed as Docker containers on a dedicated Linux VPS, with Traefik as the reverse proxy. Notifications are delivered through Telegram and WhatsApp, ensuring you stay informed without being glued to a screen.

---

## System Overview

### High-Level Architecture

The system follows a layered architecture pattern with clear separation of concerns:

| Layer | Description |
|-------|-------------|
| **Presentation** | Angular dashboard for monitoring, configuration, and analytics |
| **API Gateway** | ASP.NET Core Web API with SignalR for real-time updates |
| **Application** | Trading engine, alert engine, journal service, notification service |
| **Integration** | cTrader Open API adapter, Telegram Bot API, WhatsApp Cloud API, OpenCode Zen API |
| **Data** | PostgreSQL with schema separation for trades, alerts, analytics, and configuration |

### Architecture Diagram

```
┌─────────────────────────────────┐
│   IC Markets / cTrader          │
│   (Demo or Live Account)        │
└────────────┬────────────────────┘
             │  gRPC/Protobuf (Price Streams, Account Data, Orders)
             ▼
┌─────────────────────────────────────────────────┐
│   ASP.NET Core Backend                          │
│   ├── cTrader API Adapter (gRPC client)         │
│   ├── Alert Engine (condition evaluator)        │
│   ├── Trade Journal Service (auto-logger)       │
│   ├── Order Manager (semi-auto execution)       │
│   ├── AI Analysis Service (OpenCode Zen)────────┼──► OpenCode Zen API (GLM 4.7)
│   └── Notification Service                      │
│            │              │                     │
│       Telegram       WhatsApp                   │
└────────────┬────────────────────────────────────┘
             │
┌────────────▼────────────────────┐
│   PostgreSQL Database           │
│   ├── trading schema            │
│   ├── alerts schema             │
│   ├── journal schema            │
│   ├── analytics schema          │
│   ├── config schema             │
│   └── market_data schema        │
└────────────┬────────────────────┘
             │
             ▼
┌─────────────────────────────────┐
│   Angular Dashboard             │
│   ← SignalR WebSocket           │
└─────────────────────────────────┘
```

---

## Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Frontend | Angular 19+ | Dashboard, chart visualization, alert configuration |
| Backend | ASP.NET Core 10 (.NET 10 LTS) | REST API, SignalR hub, background services |
| Database | PostgreSQL 18 | Trade history, alert configs, analytics data |
| Cache | Redis 7 | Distributed caching, rate limiter backing store |
| Broker API | cTrader Open API | Price streaming, account data, order execution |
| Messaging | Telegram Bot API | Primary notification channel, command interface |
| Messaging | WhatsApp Cloud API | Secondary notification channel |
| Real-time | SignalR | Live price updates and alerts to Angular dashboard |
| Reverse Proxy | Traefik v3.6 | SSL termination, routing, auto-certificates |
| Containerization | Docker + Compose | Service orchestration, isolation, deployment |
| Monitoring | Prometheus + Grafana | Metrics collection, dashboards, alerting |
| AI | OpenCode Zen API | Market analysis, news summarization, sentiment — provider-agnostic design allows switching models |

### Why cTrader Open API?

| Feature | cTrader Open API | MetaTrader (MQL) |
|---------|-----------------|------------------|
| Protocol | gRPC/Protobuf (modern, fast) | Proprietary MQL language |
| Language Support | Any language (C#, Python, JS, etc.) | MQL only (C-like) |
| Web Integration | Native — designed for web apps | Requires bridges/workarounds |
| Real-time Data | Native streaming via gRPC | Polling or local EA only |
| Documentation | Well-documented, open Protobuf schemas | Fragmented across versions |
| .NET Compatibility | Excellent — native gRPC support in .NET | Requires external libraries |

---

## Database Design

Following the schema separation pattern, the trading app uses dedicated PostgreSQL schemas for logical separation:

| Schema | Purpose | Key Tables |
|--------|---------|------------|
| `trading` | Core trading data synced from cTrader | `positions`, `orders`, `deals`, `symbols`, `accounts` |
| `alerts` | Alert configurations and trigger history | `alert_rules`, `alert_triggers`, `alert_conditions` |
| `journal` | Trade journal with analytics metadata | `trade_entries`, `tags`, `notes`, `screenshots` |
| `analytics` | Aggregated performance metrics | `daily_stats`, `pair_stats`, `strategy_stats`, `equity_snapshots` |
| `config` | User preferences and system settings | `user_settings`, `notification_prefs`, `watchlists` |
| `market_data` | Historical price data for backtesting | `candles`, `tick_data`, `economic_events` |

---

## Core Services

### cTrader API Adapter

A background service (`IHostedService`) that maintains a persistent gRPC connection to cTrader's Open API. It handles authentication, reconnection, and translates Protobuf messages into domain events consumed by other services.

**Key responsibilities:**

- Authenticate via OAuth2 with cTrader account credentials
- Subscribe to real-time price streams for watchlist symbols
- Monitor account events (new positions, closed trades, margin changes)
- Execute orders when approved via the semi-automated workflow
- Handle connection drops with automatic reconnection and state recovery

**Key files:**

```
Services/
├── CTrader/
│   ├── CTraderApiAdapter.cs          # Main background service (IHostedService)
│   ├── ICTraderAuthService.cs        # Auth interface
│   ├── CTraderConnectionManager.cs   # gRPC connection lifecycle
│   ├── CTraderPriceStream.cs         # Price subscription handler
│   ├── CTraderAccountStream.cs       # Account events handler
│   ├── CTraderOrderExecutor.cs       # Order placement/modification
│   ├── CTraderSymbolResolver.cs      # Symbol ID ↔ name resolution
│   ├── CTraderHistoricalData.cs      # OHLCV candle retrieval
│   └── CTraderConversions.cs         # Unit conversion helpers
```

### Alert Engine

Evaluates incoming price data against user-defined alert rules. Supports multiple condition types:

- **Price alerts:** crosses above/below a level, percentage change within timeframe
- **Indicator alerts:** RSI overbought/oversold, MACD crossover, Bollinger Band touch
- **Composite alerts:** multiple conditions combined with AND/OR logic
- **Time-based alerts:** market open/close, economic calendar events

When conditions are met, the engine publishes an event that the Notification Service picks up and delivers via Telegram and WhatsApp.

**Key files:**

```
Services/
├── Alerts/
│   ├── AlertEngine.cs                # Main evaluation loop (IHostedService)
│   ├── AlertRuleRepository.cs        # CRUD for alert configurations
│   ├── Conditions/
│   │   ├── PriceCondition.cs         # Price level/change checks
│   │   └── IndicatorCondition.cs     # RSI, MACD, Bollinger
│   └── Indicators/
│       └── RsiCalculator.cs          # RSI calculation
```

### Trade Journal Service

Automatically captures every trade from cTrader and enriches it with analytics metadata:

- Entry/exit prices, timestamps, lot size, and P&L are recorded automatically
- Risk/reward ratio calculated from stop-loss and take-profit levels
- Trade duration, swap fees, and commission tracked
- Manual tagging support (e.g., strategy name, setup type, emotional state)
- Daily/weekly/monthly performance aggregations computed automatically

**Key files:**

```
Services/
├── Journal/
│   ├── TradeJournalService.cs        # Listens to account events, logs trades
│   ├── TradeEnricher.cs              # Calculates R:R, duration, costs
│   ├── AnalyticsAggregator.cs        # Computes daily/weekly/monthly stats
│   └── JournalRepository.cs          # CRUD + query methods
```

### Order Manager (Semi-Automated)

Provides a human-in-the-loop workflow for trade execution:

1. You define entry conditions, position size rules, and risk parameters
2. When conditions are met, the system prepares a complete order (symbol, direction, lot size, SL, TP)
3. A confirmation request is sent to Telegram with all order details
4. You approve or reject with a single tap; approved orders execute immediately
5. All pending and executed orders are logged for audit and review

**Key files:**

```
Services/
├── Orders/
│   ├── OrderManager.cs               # Strategy rule evaluator + execution
│   ├── PositionSizer.cs              # Lot size from risk % and SL distance
│   ├── ApprovalTokenStore.cs         # In-memory pending order store
│   └── RiskGuard.cs                  # Max position, daily loss, correlation checks
```

### Notification Service

A unified notification gateway that delivers messages through multiple channels:

| Channel | Use Case | Integration |
|---------|----------|-------------|
| Telegram | Primary: alerts, trade confirmations, commands | Telegram Bot API (bidirectional) |
| WhatsApp | Secondary: critical alerts, daily summaries | WhatsApp Cloud API via Meta Business |
| Dashboard | Real-time feed on Angular UI | SignalR WebSocket |

Telegram also serves as a command interface, allowing you to check account status, list open positions, or approve trades directly from the chat.

**Key files:**

```
Services/
├── Notifications/
│   ├── NotificationService.cs        # Unified gateway (SignalR + channels)
│   ├── TelegramBotService.cs         # Bot API + command handler (IHostedService)
│   └── WhatsAppService.cs            # Cloud API integration
```

---

## Docker Deployment

### Container Architecture

All services run as Docker containers orchestrated via Docker Compose:

| Container | Image | Port | Resources |
|-----------|-------|------|-----------|
| `traefik` | traefik:v3.6 | 80, 443 | 128MB RAM |
| `trading-api` | Custom .NET 10 | 8080 (internal) | 512MB RAM |
| `trading-ui` | nginx:alpine | 80 (internal) | 64MB RAM |
| `postgres` | postgres:18-alpine | 5432 (internal) | 1GB RAM |
| `redis` | redis:7-alpine | 6379 (internal) | 128MB RAM |
| `prometheus` | prom/prometheus | 9090 (internal) | 256MB RAM |
| `grafana` | grafana/grafana | 3000 (internal) | 256MB RAM |

Total estimated resource usage: ~2.3GB RAM out of 8GB available.

### Docker Compose Structure

```yaml
# docker-compose.yml (simplified — see docker-compose.override.yml for local dev)
services:
  traefik:    # Reverse proxy with auto-TLS
  trading-api:
    build: ./src/TradingAssistant.Api
    depends_on:
      postgres: { condition: service_healthy }
      redis:    { condition: service_started }
  trading-ui:
    build: ./src/trading-ui
  postgres:
    image: postgres:18-alpine
    healthcheck: ...
  redis:
    image: redis:7-alpine
    command: redis-server --appendonly yes
  prometheus:
    image: prom/prometheus:latest
  grafana:
    image: grafana/grafana:latest
```

A `docker-compose.override.yml` provides local development defaults (ports, dev credentials, Docker profiles). See [LOCAL-DEVELOPMENT.md](LOCAL-DEVELOPMENT.md) for usage.

### Traefik Routes

| Route | Target |
|-------|--------|
| `trading.yourdomain.com` | Angular dashboard (trading-ui container) |
| `api.trading.yourdomain.com` | ASP.NET Core API (trading-api container) |
| `api.trading.yourdomain.com/hub` | SignalR WebSocket endpoint |

---

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| Authentication | JWT bearer tokens with HMAC-SHA256; configurable expiry; constant-time password comparison |
| API Credentials | cTrader OAuth2 tokens stored in DB; secrets via environment variables, never committed |
| Database Access | PostgreSQL only accessible within Docker network; no external port exposure in production |
| HTTPS | All external traffic encrypted via Traefik with auto-renewed Let's Encrypt certificates |
| Trade Execution | Human-in-the-loop approval required for all orders; no fully autonomous trading |
| Telegram Bot | Bot restricted to your chat ID only; commands from other users are ignored |
| Rate Limiting — Global | Fixed-window: 100 requests/minute per IP across all endpoints |
| Rate Limiting — Auth | Fixed-window: 10 requests/minute on `/api/auth/login` |
| Rate Limiting — Trading | Fixed-window: 10 requests/minute on order execution endpoints (open, close, modify, approve) |
| Pagination Limits | All history endpoints clamp `limit` to 1–500 to prevent unbounded queries |
| Data Integrity | Explicit DB transactions on multi-step operations (order fills, journal recording, analytics) |
| HTTP Timeouts | AI API client configured with 30s timeout to prevent connection pool exhaustion |
| Background Tasks | Bounded task queue (capacity 100) prevents unbounded memory growth |
| Backup | Daily PostgreSQL backups via pg_dump; VPS snapshot available |

---

## Development Phases

### Phase 1: Foundation + Smart Alerts (Weeks 1–4) ✅

**Goal:** Get the core infrastructure running with real-time price monitoring and alert notifications.

**Backend:** Set up ASP.NET Core project with Docker Compose, PostgreSQL schemas, and cTrader Open API connection. Implement OAuth2 authentication flow with cTrader.

**Alert Engine:** Build the condition evaluator supporting price-level alerts, percentage-change alerts, and basic indicator alerts (RSI, MACD). Configure Telegram Bot for bidirectional communication.

**Frontend:** Basic Angular dashboard with watchlist view, alert rule configuration form, and alert history log.

**Deliverable:** A working system that monitors prices and sends you Telegram/WhatsApp alerts based on your rules.

### Phase 2: Trade Journal + Analytics (Weeks 5–8) ✅

**Goal:** Automatically log all trades and provide performance insights.

**Journal Service:** Subscribe to cTrader account events to capture every trade automatically. Store enriched trade data with calculated metrics.

**Analytics Engine:** Build aggregation queries for win rate, average R:R, best/worst pairs, performance by day of week, drawdown tracking.

**Frontend:** Analytics dashboard with charts (equity curve, win rate over time, pair performance heatmap). Trade list with filtering, tagging, and note-taking.

**Deliverable:** Complete automated trade journal with performance analytics dashboard.

### Phase 3: Semi-Automated Execution (Weeks 9–12) ✅

**Goal:** Move from alerts to actionable trade proposals with one-tap execution.

**Order Manager:** Define strategy rules (entry conditions, position sizing based on account balance and risk percentage, stop-loss/take-profit calculation).

**Execution Flow:** When conditions are met, prepare the order and send a rich Telegram message with all details. Implement approve/reject buttons. Execute approved orders via cTrader API.

**Risk Controls:** Maximum position size limits, daily loss limits, correlation checks (avoid overexposure to correlated pairs).

**Deliverable:** Semi-automated trading system with human approval workflow.

### Phase 4: AI Integration + News Monitoring (Weeks 13–16) ✅

**Goal:** Add intelligence layer for market context and sentiment analysis using OpenCode Zen.

**AI Provider:** OpenCode Zen API with GLM 4.7 as the primary model. The backend uses a provider-agnostic service layer with OpenAI-compatible API format, allowing model/provider switching via configuration.

**AI Service Architecture:**

```
Services/
├── AI/
│   ├── IAiAnalysisService.cs            # Provider-agnostic interface
│   ├── OpenCodeZenService.cs            # OpenCode Zen API client (OpenAI-compatible)
│   └── MarketSessionService.cs          # Trading session time utilities
├── Analysis/
│   └── ScheduledAnalysisService.cs      # Cron-style watchlist analysis (IHostedService)
```

**Configuration (appsettings.json):**

```json
{
  "AiProvider": {
    "BaseUrl": "https://api.opencode.ai/v1",
    "Model": "opencode/glm-4.7",
    "ApiKey": "${OPENCODE_ZEN_API_KEY}",
    "MaxTokens": 4096,
    "Temperature": 0.3
  }
}
```

**Tool Use (Function Calling):** Define custom tools that the AI model can call to interact with your trading data:

- `get_open_positions` — current positions with P&L
- `get_account_balance` — equity, margin, free margin
- `get_price_history` — OHLCV candles for a symbol
- `get_economic_calendar` — upcoming events for today/week
- `get_trade_history` — recent closed trades with stats

**Key Features:**

- **Morning Market Briefing:** Daily watchlist analysis sent via Telegram at market open
- **Alert Enrichment:** When an alert fires, AI adds market context explaining why the level matters
- **Trade Journal Review:** Weekly batch analysis of all trades, identifying patterns and mistakes
- **News Monitoring:** RSS feed scanning cross-referenced with watchlist and open positions
- **Pre-Trade Analysis:** Before semi-auto trade confirmation, AI provides supporting/opposing factors

**Structured Output Example:**

```json
{
  "pair": "EURUSD",
  "bias": "bearish",
  "confidence": 0.72,
  "key_levels": { "support": 1.0845, "resistance": 1.0920 },
  "risk_events": ["ECB rate decision Thursday 12:45 GMT"],
  "recommendation": "reduce_exposure",
  "reasoning": "Price rejected from weekly resistance with bearish divergence on 4H RSI..."
}
```

**Model Selection Strategy:**

| Task | Model | Reason |
|------|-------|--------|
| Alert enrichment | GLM 4.7 Flash | Fast, cheap, simple context |
| Daily briefing | GLM 4.7 | Good analysis quality |
| Weekly trade review | GLM 4.7 | Deeper reasoning needed |
| Complex correlation analysis | GLM 4.7 (extended thinking) | Multi-step reasoning |

**Deliverable:** AI-enhanced trading assistant with market intelligence capabilities.

> **Data Privacy Note:** During GLM 4.7's free period on OpenCode Zen, collected data may be used for model improvement. Once trading with real money, switch to a paid model where zero-retention policy applies.

---

## Testing Strategy

The application is tested in stages using cTrader demo accounts before any real money is involved.

### Stage 1: Development (Demo Account)

Use an IC Markets cTrader demo account (free, preloaded with virtual funds, real-time market data). All features are built and tested against this account. The cTrader Open API works identically for demo and live accounts.

**Setup:**

- Create a cTrader demo account from the IC Markets Client Area
- Use the cTrader Open API Playground for quick token generation during development
- All alert rules, order execution, and journal capture tested with virtual trades
- Demo accounts remain active as long as there's activity within 30 days

### Stage 2: Validation (Demo Account, 2–4 Weeks)

Run the full system as if it were live. Monitor for:

- Alert accuracy: do alerts fire at the correct price levels and conditions?
- Journal completeness: are all trades captured with correct metadata?
- Telegram reliability: do notifications arrive consistently and promptly?
- Semi-auto execution: does the approve/reject workflow execute orders correctly?
- System stability: memory usage, reconnection handling, uptime over multi-day runs

### Stage 3: Live Micro (Real Account, Minimum Size)

Switch to the live IC Markets account with minimal risk:

- Trade with 0.01 lots (micro lots) only
- Test real execution: slippage, spread, commission accuracy
- Verify journal captures real trade costs correctly
- Monitor for any differences between demo and live behavior

**Configuration change:** Only the cTrader account credentials in `.env` need to change — the API calls are identical.

### Stage 4: Scale Up (Gradual)

Increase position sizes gradually as confidence builds. Monitor all analytics for consistency with demo testing results.

### Environment Configuration

```env
# .env
CTRADER_ENVIRONMENT=demo          # Switch to "live" when ready
CTRADER_ACCOUNT_ID=12345678       # Demo account ID
CTRADER_CLIENT_ID=your_client_id
CTRADER_CLIENT_SECRET=your_secret
```

---

## Telegram Bot Commands

| Command | Description |
|---------|-------------|
| `/status` | Show account balance, equity, margin, and open P&L |
| `/positions` | List all open positions with current P&L |
| `/alerts` | List active alert rules |
| `/alert EURUSD > 1.0900` | Create a quick one-shot price alert |
| `/today` | Show today's trading summary (trades, P&L, win rate) |
| `/week` | Show this week's performance summary |
| `/calendar` | Show upcoming economic events for today |
| `/analyze EURUSD` | Run AI market analysis for a symbol |
| `/briefing` | Generate daily market briefing for watchlist symbols |
| `/review 123` | AI review of a specific trade by ID |
| `/news EURUSD` | AI news sentiment analysis for a symbol |
| `/help` | Show all available commands |

The bot also supports inline approve/reject buttons for order confirmations sent via the semi-automated execution workflow.

---

## Estimated Monthly Costs

| Service | Cost (USD) | Notes |
|---------|-----------|-------|
| VPS (existing) | $0 | Already owned — 4 vCPU, 8GB RAM |
| cTrader Open API | Free | Included with IC Markets account |
| Telegram Bot API | Free | Unlimited messages for bots |
| WhatsApp Cloud API | ~$0–$5 | 1,000 free conversations/month |
| OpenCode Zen — GLM 4.7 (Phase 4) | ~$1–$5 | Pay-as-you-go, ~$0.60/$2.20 per 1M input/output tokens |
| OpenCode Zen — GLM 4.7 Flash (Phase 4) | ~$0–$1 | For lightweight alert enrichment, $0.06/$0.40 per 1M tokens |
| GLM Coding Lite (development) | $3 | IDE coding assistance during development |
| Domain + SSL | Free | Let's Encrypt via Traefik |

**Total for Phases 1–3:** Effectively $0 beyond the existing VPS.

**Total for Phase 4:** ~$1–$5/month for AI analysis via OpenCode Zen.

---

## Next Steps

All four development phases are complete. Remaining work focuses on hardening and operations:

1. **Credential rotation** — Rotate any secrets that were previously committed to git history
2. **Circuit breaker** — Add Polly circuit breaker policies to external service calls (cTrader, Telegram, AI)
3. **Test coverage** — Add tests for rate limiting, pagination clamping, transaction rollback, and BackgroundTaskQueue
4. **Redis Streams event bus** — Decouple price stream producers from consumers for better resilience
5. **OpenTelemetry tracing** — End-to-end distributed tracing across services
6. **Live validation** — Run the full system against a demo account for 2–4 weeks before switching to live

---

## Architecture Recommendations

The following enhancements should be considered during implementation to improve resilience, performance, and operability.

### Message Broker for Event Decoupling

> **Status:** Partially implemented. Redis is deployed for caching and rate limiting. Event bus via Redis Streams remains a future enhancement.

The current architecture uses direct service-to-service communication for price events. For higher throughput, introducing Redis Streams as an event bus would improve resilience and decoupling.

**Recommendation:** Use Redis Streams as an event bus (Redis container already deployed).

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│ cTrader Adapter │────►│  Redis Streams  │────►│  Alert Engine   │
│ (price events)  │     │  (event bus)    │────►│  Journal Svc    │
└─────────────────┘     └─────────────────┘────►│  Order Manager  │
                                                └─────────────────┘
```

**Benefits:**
- Price stream producer is decoupled from consumers
- Consumers can process at their own pace without backpressure on the adapter
- Failed consumers don't block other services
- Event replay capability for debugging and recovery

**Implementation:** Add a `redis` container to Docker Compose; use `StackExchange.Redis` in .NET.

### Database Scaling Strategy

Under heavy price streaming, analytics aggregations could compete with real-time writes. Plan for scaling early.

**Recommendations:**

| Concern | Mitigation |
|---------|------------|
| Connection exhaustion | Use `Npgsql` connection pooling with `MaxPoolSize=100` and `MinPoolSize=10` |
| Read/write contention | Separate read-heavy analytics queries to a read replica (Phase 2+) |
| Large table performance | Partition `market_data.candles` and `market_data.tick_data` by date |
| Aggregation load | Pre-compute daily stats via scheduled background job, not on-demand queries |

**Connection string example:**
```
Host=postgres;Database=trading_assistant;Username=...;Password=...;Pooling=true;MinPoolSize=10;MaxPoolSize=100;
```

### Event-Driven Alert Engine

> **Status:** Implemented. `AlertEngine` uses a `ConcurrentDictionary<string, List<AlertRule>>` keyed by symbol, subscribed to `CTraderPriceStream.OnPriceUpdate` events.

The alert engine avoids a synchronous polling loop by using event-driven selective evaluation.

```csharp
// Instead of: foreach (var rule in allRules) { Evaluate(rule, price); }
// Use: subscribe only to symbols with active alerts

public class AlertEngine : IHostedService
{
    private readonly ConcurrentDictionary<string, List<AlertRule>> _rulesBySymbol;

    public async Task OnPriceUpdate(string symbol, decimal price)
    {
        if (_rulesBySymbol.TryGetValue(symbol, out var rules))
        {
            foreach (var rule in rules)
                await EvaluateAsync(rule, price);
        }
    }
}
```

**Benefits:**
- Only evaluates rules for symbols that received updates
- Scales linearly with active symbols, not total rules
- Easier to parallelize per-symbol processing

### Observability Stack

> **Status:** Partially implemented. Serilog structured logging, Prometheus metrics endpoint (`/metrics`), and Grafana dashboards are deployed. OpenTelemetry distributed tracing remains a future enhancement.

Critical for debugging issues in a trading system where timing and state matter.

| Component | Technology | Purpose |
|-----------|------------|---------|
| Logging | Serilog + Seq (or Loki) | Structured logs with correlation IDs |
| Metrics | Prometheus + Grafana | Price latency, order execution time, alert trigger rate |
| Tracing | OpenTelemetry | End-to-end request tracing across services |
| Alerting | Grafana Alerts | System health alerts (connection drops, high latency) |

**Key metrics to track:**

```
trading_price_latency_ms          # Time from cTrader event to processing
trading_alert_evaluations_total   # Counter by symbol and rule type
trading_orders_executed_total     # Counter by symbol and outcome
trading_ctrader_connection_status # Gauge: 1=connected, 0=disconnected
trading_telegram_delivery_latency_ms
```

**Docker Compose addition:**

```yaml
  prometheus:
    image: prom/prometheus:latest
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    restart: unless-stopped

  grafana:
    image: grafana/grafana:latest
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_PASSWORD}
    volumes:
      - grafana-data:/var/lib/grafana
    restart: unless-stopped
```

### cTrader Reconnection Strategy

> **Status:** Implemented. `CTraderConnectionManager` handles reconnection with exponential backoff. `TelegramBotService` also uses exponential backoff for initial connection.

Connection drops during volatile markets need careful handling to avoid missed data or duplicate orders.

```
┌─────────────┐    auth success    ┌─────────────┐
│ Disconnected│───────────────────►│  Connected  │
└──────┬──────┘                    └──────┬──────┘
       │                                  │
       │ reconnect                        │ connection lost
       │ (exponential backoff)            │
       │                                  ▼
       │                           ┌─────────────┐
       └───────────────────────────│ Reconnecting│
                                   └─────────────┘
```

**Key behaviors:**

| Scenario | Handling |
|----------|----------|
| Clean disconnect | Immediate reconnection attempt |
| Auth failure | Log error, alert via Telegram, pause reconnection |
| Repeated failures | Exponential backoff: 1s → 2s → 4s → 8s → max 60s |
| Reconnection success | Re-subscribe to all price streams, reconcile account state |
| Data gap detection | Request historical ticks/candles to fill gaps after reconnect |
| Pending orders | On reconnect, query order status before allowing new orders |

**Implementation:**

```csharp
public class ReconnectionPolicy
{
    private int _attemptCount = 0;
    private readonly int _maxDelaySeconds = 60;

    public TimeSpan GetNextDelay()
    {
        var delay = Math.Min(Math.Pow(2, _attemptCount), _maxDelaySeconds);
        _attemptCount++;
        return TimeSpan.FromSeconds(delay);
    }

    public void Reset() => _attemptCount = 0;
}
```

### Circuit Breaker Pattern

> **Status:** Not yet implemented. HTTP timeouts (30s on AI client) provide basic protection; full Polly circuit breakers remain a future enhancement.

Protect against cascading failures when external services (cTrader, Telegram, WhatsApp) are degraded.

**Recommendation:** Use Polly for circuit breaker and retry policies.

```csharp
// In Program.cs or service registration
services.AddHttpClient<ITelegramBotService, TelegramBotService>()
    .AddPolicyHandler(Policy
        .Handle<HttpRequestException>()
        .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromMinutes(1),
            onBreak: (_, duration) =>
                Log.Warning("Telegram circuit open for {Duration}", duration),
            onReset: () =>
                Log.Information("Telegram circuit closed")
        ));
```

**Circuit breaker settings by service:**

| Service | Failure Threshold | Break Duration | Fallback |
|---------|-------------------|----------------|----------|
| cTrader API | 3 failures | 30 seconds | Queue orders, alert user |
| Telegram | 5 failures | 1 minute | Log locally, retry queue |
| WhatsApp | 5 failures | 2 minutes | Skip (non-critical) |
| OpenCode Zen | 3 failures | 1 minute | Serve cached analysis, skip enrichment |

**Order execution safeguard:**

```csharp
public class RiskGuard
{
    public bool CanExecuteOrder()
    {
        if (_ctraderCircuit.State == CircuitState.Open)
        {
            _logger.Warning("Order blocked: cTrader circuit is open");
            return false;
        }
        return true;
    }
}
```

---

## Project Structure

```
intelligent-trading-assistant/
├── docker-compose.yml
├── docker-compose.override.yml        # Local dev overrides (ports, profiles, dev creds)
├── .env.example                       # Template for environment variables
├── prometheus.yml                     # Production Prometheus config
├── prometheus.local.yml               # Local dev Prometheus config
├── traefik/
│   └── traefik.yml
├── grafana/
│   └── provisioning/
│       └── datasources/
│           └── prometheus.yml
├── scripts/
│   ├── backup.sh
│   ├── deploy.sh
│   └── init-db.sql
├── src/
│   ├── TradingAssistant.Api/
│   │   ├── Dockerfile
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── AiController.cs        # AI analysis, briefings, candles
│   │   │   ├── AlertsController.cs    # Alert CRUD + trigger history
│   │   │   ├── AnalyticsController.cs # Performance overview, equity curve
│   │   │   ├── AuthController.cs      # JWT login + cTrader OAuth
│   │   │   ├── JournalController.cs   # Trade journal, tags, notes
│   │   │   ├── OrdersController.cs    # Pending order approval/rejection
│   │   │   ├── PositionsController.cs # Positions, account, symbols, orders
│   │   │   └── WatchlistController.cs # Watchlist + analysis settings
│   │   ├── Hubs/
│   │   │   └── TradingHub.cs          # SignalR hub for real-time updates
│   │   ├── Services/
│   │   │   ├── BackgroundTaskQueue.cs # Bounded channel-based task queue
│   │   │   ├── SymbolCategorizer.cs   # Forex/Metal/Index/Crypto classification
│   │   │   ├── AI/
│   │   │   ├── Alerts/
│   │   │   ├── Analysis/
│   │   │   ├── CTrader/
│   │   │   ├── Journal/
│   │   │   ├── Notifications/
│   │   │   └── Orders/
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   └── Migrations/            # 7 EF Core migrations
│   │   └── Models/
│   │       ├── Trading/               # Account, Position, Order, Deal, Symbol, WatchlistSymbol
│   │       ├── Alerts/                # AlertRule, AlertCondition, AlertTrigger
│   │       ├── Journal/               # TradeEntry, TradeTag, TradeNote
│   │       └── Analytics/             # DailyStats, PairStats, EquitySnapshot, AnalysisSnapshot
│   └── trading-ui/
│       ├── Dockerfile
│       ├── nginx.conf
│       └── src/app/
│           ├── app.component.ts       # Shell with sidebar navigation
│           ├── app.routes.ts          # Lazy-loaded routes
│           ├── auth/                  # Login, AuthGuard, AuthInterceptor, AuthService
│           ├── dashboard/             # Real-time account overview
│           ├── positions/             # Open positions + order placement
│           ├── alerts/                # Alert rule management
│           ├── journal/               # Trade journal browser
│           ├── analytics/             # Performance charts and stats
│           ├── ai-analysis/           # AI market analysis + trade chart
│           ├── watchlist/             # Watchlist + scan settings
│           └── shared/
│               ├── components/        # ConfirmDialog, NotificationToast
│               ├── interceptors/      # ErrorInterceptor (global HTTP error handling)
│               └── services/          # SignalR, Notification, ConfirmDialog services
├── tests/
│   └── TradingAssistant.Tests/
│       ├── AI/                        # OpenCodeZenService tests
│       ├── Alerts/                    # PriceCondition, IndicatorCondition tests
│       ├── CTrader/                   # CTraderPriceStream tests
│       ├── Indicators/                # RSI, MACD, Bollinger calculator tests
│       ├── Journal/                   # AnalyticsAggregator, TradeEnricher tests
│       └── Orders/                    # PositionSizer, RiskGuard tests
└── docs/
    ├── ARCHITECTURE.md
    ├── LOCAL-DEVELOPMENT.md
    └── API.md
```

---

> **Disclaimer:** This system is designed as a trading assistant tool, not a fully autonomous trading bot. All trade execution requires human approval. Trading forex and CFDs carries significant risk, and past performance is not indicative of future results. Always trade with money you can afford to lose.
