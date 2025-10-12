#!/usr/bin/env bash
set -Eeuo pipefail

ENV="/opt/birthday/.env"
[[ -f "$ENV" ]] && source "$ENV" || true

if [[ -z "${DUCKDNS_TOKEN:-}" || -z "${DOMAIN:-}" ]]; then
  echo "[duckdns] Skip: DUCKDNS_TOKEN or DOMAIN is empty"
  exit 0
fi

SUB="${DOMAIN%%.duckdns.org}"
if [[ "$SUB" == "$DOMAIN" ]]; then
  echo "[duckdns] Not a duckdns domain, skip."
  exit 0
fi

URL="https://www.duckdns.org/update?domains=$SUB&token=$DUCKDNS_TOKEN&ip="
curl -fsS "$URL" && echo "[duckdns] updated ok" || echo "[duckdns] update failed" >&2
