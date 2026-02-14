# API Reference

Base URL: `http://localhost:5000/api` (local) or `https://api.optimistgm.xyz/api` (production)

All endpoints except `/api/auth/*` require a JWT bearer token in the `Authorization` header.

**Rate Limiting:**

| Policy | Limit | Applied To |
|--------|-------|------------|
| `fixed` | 100 req/min | All endpoints (global default) |
| `auth` | 10 req/min | `POST /api/auth/login` |
| `trading` | 10 req/min | Order execution: open, close, modify, approve |

---

## Auth

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| POST | `/auth/login` | No | `auth` | Login with password, returns JWT |
| GET | `/auth/ctrader` | No | `fixed` | Redirect to cTrader OAuth |
| GET | `/auth/ctrader/callback` | No | `fixed` | cTrader OAuth callback |
| GET | `/auth/ctrader/status` | No | `fixed` | Check cTrader token status |

**POST /auth/login**

```json
{ "password": "string" }
```

Returns: `{ "token": "eyJ..." }`

---

## Positions

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/positions` | Yes | `fixed` | List open positions |
| GET | `/positions/{id}` | Yes | `fixed` | Get position by ID |
| GET | `/positions/history` | Yes | `fixed` | Closed position history |
| POST | `/positions/open` | Yes | `trading` | Open a new position |
| POST | `/positions/{id}/close` | Yes | `trading` | Close a position |
| POST | `/positions/{id}/modify` | Yes | `trading` | Modify SL/TP |
| GET | `/positions/account` | Yes | `fixed` | Account balance and equity |
| GET | `/positions/summary` | Yes | `fixed` | Open position summary |
| GET | `/positions/symbols` | Yes | `fixed` | Available trading symbols |

**GET /positions/history** — Query parameters:

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `symbol` | string | — | Filter by symbol |
| `from` | DateTime | — | Start date filter |
| `limit` | int | 50 | Max results (clamped 1–500) |

**POST /positions/open**

```json
{
  "symbol": "EURUSD",
  "direction": "Buy",
  "orderType": "Market",
  "volume": 0.01,
  "price": null,
  "stopLoss": 1.0800,
  "takeProfit": 1.1000
}
```

- `direction`: `"Buy"` or `"Sell"`
- `orderType`: `"Market"`, `"Limit"`, or `"Stop"`
- `price`: Required for Limit and Stop orders

**POST /positions/{id}/modify**

```json
{ "stopLoss": 1.0850, "takeProfit": 1.1050 }
```

---

## Orders

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/orders/pending` | Yes | `fixed` | List pending approval orders |
| POST | `/orders/{token}/approve` | Yes | `trading` | Approve and execute order |
| POST | `/orders/{token}/reject` | Yes | `fixed` | Reject pending order |

---

## Alerts

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/alerts` | Yes | `fixed` | List alerts (with optional filters) |
| GET | `/alerts/{id}` | Yes | `fixed` | Get alert by ID |
| POST | `/alerts` | Yes | `fixed` | Create alert rule |
| PUT | `/alerts/{id}` | Yes | `fixed` | Update alert rule |
| DELETE | `/alerts/{id}` | Yes | `fixed` | Delete alert rule |
| GET | `/alerts/history` | Yes | `fixed` | Alert trigger history |

**GET /alerts** — Query parameters:

| Param | Type | Description |
|-------|------|-------------|
| `activeOnly` | bool | Filter to active alerts only |
| `symbol` | string | Filter by symbol |

**GET /alerts/history** — Query parameters:

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `limit` | int | 50 | Max results (clamped 1–500) |
| `since` | DateTime | — | Start date filter |

**POST /alerts**

```json
{
  "symbol": "EURUSD",
  "name": "EURUSD above 1.09",
  "description": "optional description",
  "type": "Price",
  "autoPrepareOrder": false,
  "aiEnrichEnabled": true,
  "notifyTelegram": true,
  "notifyDashboard": true,
  "maxTriggers": 1,
  "conditions": [
    {
      "type": "PriceLevel",
      "operator": "GreaterThan",
      "value": 1.0900,
      "combineWith": null
    }
  ]
}
```

---

## AI Analysis

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/ai/analyze/{symbol}` | Yes | `fixed` | AI market analysis |
| GET | `/ai/review/{tradeId}` | Yes | `fixed` | AI trade review |
| POST | `/ai/briefing` | Yes | `fixed` | Generate daily briefing |
| GET | `/ai/news/{symbol}` | Yes | `fixed` | News sentiment analysis |
| GET | `/ai/history` | Yes | `fixed` | Analysis snapshot history |
| GET | `/ai/candles/{symbol}` | Yes | `fixed` | OHLCV candle data |

**GET /ai/analyze/{symbol}** — Query parameters:

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `timeframe` | string | `H4` | M1, M5, M15, M30, H1, H4, D1 |

**GET /ai/history** — Query parameters:

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `symbol` | string | — | Filter by symbol |
| `limit` | int | 50 | Max results (clamped 1–500) |

**GET /ai/candles/{symbol}** — Query parameters:

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `timeframe` | string | `H4` | Candle period |
| `count` | int | 100 | Number of candles (10–500) |

**POST /ai/briefing**

```json
{ "watchlist": ["EURUSD", "GBPUSD", "XAUUSD"] }
```

---

## Journal

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/journal` | Yes | `fixed` | List trade entries |
| GET | `/journal/{id}` | Yes | `fixed` | Get trade with tags and notes |
| PUT | `/journal/{id}` | Yes | `fixed` | Update trade metadata |
| POST | `/journal/{id}/tags` | Yes | `fixed` | Add tag to trade |
| DELETE | `/journal/{id}/tags/{tagId}` | Yes | `fixed` | Remove tag |
| POST | `/journal/{id}/notes` | Yes | `fixed` | Add note to trade |
| GET | `/journal/stats/daily` | Yes | `fixed` | Daily P&L stats |

**GET /journal** — Query parameters:

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `symbol` | string | — | Filter by symbol |
| `from` | DateTime | — | Start date |
| `to` | DateTime | — | End date |
| `limit` | int | 100 | Max results |
| `offset` | int | 0 | Pagination offset |

**PUT /journal/{id}**

```json
{
  "strategy": "Breakout",
  "setup": "London Open",
  "emotion": "Confident",
  "rating": 4
}
```

---

## Analytics

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/analytics/overview` | Yes | `fixed` | Performance overview |
| GET | `/analytics/equity-curve` | Yes | `fixed` | Equity/balance over time |
| GET | `/analytics/by-day-of-week` | Yes | `fixed` | Stats by day of week |
| GET | `/analytics/by-hour` | Yes | `fixed` | Stats by hour of day |

All analytics endpoints accept optional `from` and `to` query parameters (DateTime).

---

## Watchlist

| Method | Path | Auth | Rate Limit | Description |
|--------|------|------|------------|-------------|
| GET | `/watchlist` | Yes | `fixed` | Get watchlist + analysis settings |
| POST | `/watchlist` | Yes | `fixed` | Add symbol to watchlist |
| DELETE | `/watchlist/{id}` | Yes | `fixed` | Remove symbol from watchlist |
| PUT | `/watchlist/settings` | Yes | `fixed` | Update analysis/risk settings |

**PUT /watchlist/settings**

```json
{
  "scheduleUtcHours": [6, 14],
  "autoPrepareMinConfidence": 70,
  "maxOpenPositions": 3,
  "maxTotalVolume": 10.0,
  "maxPositionsPerSymbol": 3,
  "maxDailyLossPercent": 5.0
}
```

---

## SignalR Hub

**Endpoint:** `/hub` (WebSocket, requires JWT via `access_token` query parameter)

### Server → Client events:

| Event | Payload | Description |
|-------|---------|-------------|
| `ReceivePositionUpdate` | `PositionUpdate` | Real-time position P&L update |
| `ReceiveTradeExecuted` | `TradeNotification` | Position closed notification |
| `ReceiveAlert` | `AlertNotification` | Alert triggered |

---

## Health Check

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/health` | No | Returns 200 if API, PostgreSQL, and Redis are healthy |

## Metrics

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/metrics` | No | Prometheus metrics endpoint |
