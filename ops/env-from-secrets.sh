#!/usr/bin/env bash
set -euo pipefail
REGION="${REGION:-eu-central-1}"
DOMAIN="${DOMAIN:-bday-bot.duckdns.org}"

# Try unified secret first, fallback to individual secrets for backward compatibility
UNIFIED_SECRET=$(aws secretsmanager get-secret-value \
  --region "$REGION" \
  --secret-id "birthday-bot/all-config" \
  --query 'SecretString' \
  --output text 2>/dev/null || echo "")

if [ -n "$UNIFIED_SECRET" ]; then
  # Parse JSON from unified secret
  if command -v jq >/dev/null 2>&1; then
    TELEGRAM_TOKEN=$(echo "$UNIFIED_SECRET" | jq -r '.telegram_token')
    WEBHOOK_SECRET=$(echo "$UNIFIED_SECRET" | jq -r '.webhook_secret // ""')
    DUCKDNS_TOKEN=$(echo "$UNIFIED_SECRET" | jq -r '.duckdns_token // ""')
    MONGO_URI=$(echo "$UNIFIED_SECRET" | jq -r '.mongo_cs // .mongo_url')
  else
    # Fallback: use Python (usually available on EC2)
    TELEGRAM_TOKEN=$(python3 -c "import json, sys; print(json.load(sys.stdin)['telegram_token'])" <<< "$UNIFIED_SECRET")
    WEBHOOK_SECRET=$(python3 -c "import json, sys; print(json.load(sys.stdin).get('webhook_secret', ''))" <<< "$UNIFIED_SECRET")
    DUCKDNS_TOKEN=$(python3 -c "import json, sys; print(json.load(sys.stdin).get('duckdns_token', ''))" <<< "$UNIFIED_SECRET")
    MONGO_URI=$(python3 -c "import json, sys; d=json.load(sys.stdin); print(d.get('mongo_cs') or d.get('mongo_url', ''))" <<< "$UNIFIED_SECRET")
  fi
else
  # Fallback: read from individual secrets (backward compatibility)
  get_secret () {
    aws secretsmanager get-secret-value --region "$REGION" --secret-id "$1" \
      --query 'SecretString' --output text
  }
  
  TELEGRAM_TOKEN="$(get_secret birthday-bot/telegram-token)"
  MONGO_URI="$(get_secret birthday-bot/mongo-url)"
  WEBHOOK_SECRET="$(get_secret birthday-bot/webhook-secret || echo '')"
  DUCKDNS_TOKEN="$(get_secret birthday-bot/duckdns-token || echo '')"
fi
# Extract database name from URI (strip query parameters for Atlas compatibility)
DB_NAME="${MONGO_URI##*/}"
DB_NAME="${DB_NAME%%\?*}"  # Remove query parameters (e.g., ?retryWrites=true&w=majority)
: "${DB_NAME:=birthdays}"

# Preserve mongo-express password across .env regenerations (generate once)
ME_PASS=""
if [ -f ./.env ]; then
  ME_PASS="$(grep '^ME_CONFIG_BASICAUTH_PASSWORD=' ./.env | head -1 | cut -d= -f2- || true)"
fi
if [ -z "$ME_PASS" ]; then
  ME_PASS="$(openssl rand -hex 16)"
  echo "[info] Generated new mongo-express password: $ME_PASS"
fi

cat > ./.env <<ENV
ASPNETCORE_URLS=http://0.0.0.0:8080

# Telegram - .NET configuration (double underscore maps to nested config)
Bot__Token=${TELEGRAM_TOKEN}
Bot__WebhookSecretToken=${WEBHOOK_SECRET}

# Telegram - legacy/env variable names (for backward compatibility)
TELEGRAM_BOT_TOKEN=${TELEGRAM_TOKEN}
TELEGRAM_WEBHOOK_SECRET=${WEBHOOK_SECRET}
BOT__TOKEN=${TELEGRAM_TOKEN}
BOT__WEBHOOKSECRET=${WEBHOOK_SECRET}
BOT__WEBHOOKSECRETTOKEN=${WEBHOOK_SECRET}
Telegram__BotToken=${TELEGRAM_TOKEN}
Telegram__Token=${TELEGRAM_TOKEN}
BotConfiguration__Token=${TELEGRAM_TOKEN}

# Mongo - .NET configuration (double underscore maps to nested config)
Mongo__ConnectionString=${MONGO_URI}
Mongo__Database=${DB_NAME}

# Mongo - legacy/env variable names (for ReminderHostedService direct env read)
MONGODB_URI=${MONGO_URI}
MONGO_DBNAME=${DB_NAME}

# Domain and certificates
DOMAIN=${DOMAIN}
ACME_EMAIL=you@example.com

# DuckDNS (optional, for dynamic DNS updates)
DUCKDNS_TOKEN=${DUCKDNS_TOKEN}

# mongo-express web UI (https://<DOMAIN>/mongo/)
ME_CONFIG_MONGODB_URL=${MONGO_URI}
ME_CONFIG_BASICAUTH_USERNAME=admin
ME_CONFIG_BASICAUTH_PASSWORD=${ME_PASS}
ME_CONFIG_SITE_BASEURL=/mongo
ENV

echo "[ok] .env updated"

