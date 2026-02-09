-- Initialize PostgreSQL schemas for Trading Assistant
-- Run automatically on first container start

-- Trading schema: Core trading data synced from cTrader
CREATE SCHEMA IF NOT EXISTS trading;

-- Alerts schema: Alert configurations and trigger history
CREATE SCHEMA IF NOT EXISTS alerts;

-- Journal schema: Trade journal with analytics metadata
CREATE SCHEMA IF NOT EXISTS journal;

-- Analytics schema: Aggregated performance metrics
CREATE SCHEMA IF NOT EXISTS analytics;

-- Config schema: User preferences and system settings
CREATE SCHEMA IF NOT EXISTS config;

-- Market data schema: Historical price data for backtesting
CREATE SCHEMA IF NOT EXISTS market_data;

-- Grant usage on all schemas to the application user
GRANT USAGE ON SCHEMA trading, alerts, journal, analytics, config, market_data TO trading_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA trading, alerts, journal, analytics, config, market_data TO trading_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA trading, alerts, journal, analytics, config, market_data
    GRANT ALL PRIVILEGES ON TABLES TO trading_user;
