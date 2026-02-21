#!/usr/bin/env bash
set -euo pipefail
REGION="${REGION:-eu-central-1}"
DOMAIN="${DOMAIN:-bday-bot.duckdns.org}"

get_secret () {
  aws secretsmanager get-secret-value --region "$REGION" --secret-id "$1" \
    --query 'SecretString' --output text
}

TELEGRAM_TOKEN="$(get_secret birthday-bot/telegram-token)"
MONGO_URI="$(get_secret birthday-bot/mongo-url)"
WEBHOOK_SECRET="$(get_secret birthday-bot/webhook-secret || echo '')"
DUCKDNS_TOKEN="$(get_secret birthday-bot/duckdns-token || echo '')"
DB_NAME="${MONGO_URI##*/}"
: "${DB_NAME:=birthdays}"

cat > ./.env <<ENV
ASPNETCORE_URLS=http://0.0.0.0:8080

# Telegram (все популярные ключи)
TELEGRAM_BOT_TOKEN=${TELEGRAM_TOKEN}
BOT__TOKEN=${TELEGRAM_TOKEN}
Telegram__BotToken=${TELEGRAM_TOKEN}
Telegram__Token=${TELEGRAM_TOKEN}
BotConfiguration__Token=${TELEGRAM_TOKEN}
TELEGRAM_WEBHOOK_SECRET=${WEBHOOK_SECRET}
BOT__WEBHOOKSECRET=${WEBHOOK_SECRET}
BOT__WEBHOOKSECRETTOKEN=${WEBHOOK_SECRET}

# Mongo
Mongo__ConnectionString=${MONGO_URI}
Mongo__Database=${DB_NAME}
MONGODB_URI=${MONGO_URI}
MONGO_DBNAME=${DB_NAME}

# Domain and certificates
DOMAIN=${DOMAIN}
ACME_EMAIL=you@example.com

# DuckDNS (optional, for dynamic DNS updates)
DUCKDNS_TOKEN=${DUCKDNS_TOKEN}
ENV

echo "[ok] .env updated"

