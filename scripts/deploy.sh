#!/bin/bash
# Deployment script for Trading Assistant
# Usage: ./deploy.sh [environment]

set -euo pipefail

ENVIRONMENT="${1:-production}"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "${PROJECT_DIR}"

echo "Deploying Trading Assistant (${ENVIRONMENT})..."

# Pull latest changes
git pull origin main

# Build and restart containers
docker compose build --no-cache
docker compose down
docker compose up -d

# Wait for services to be healthy
echo "Waiting for services to start..."
sleep 10

# Run database migrations
docker compose exec -T trading-api dotnet ef database update

# Verify all containers are running
docker compose ps

echo "Deployment complete!"
