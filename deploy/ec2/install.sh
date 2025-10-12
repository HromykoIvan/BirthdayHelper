#!/usr/bin/env bash
set -Eeuo pipefail

# где мы храним наши файлы
ROOT="/opt/birthday"
BIN="/usr/local/bin"

# скачиваем все наши скрипты из репо (raw)
# при желании замени на твой бекет/asset — это просто, чтобы не вручную класть.
RAW="https://raw.githubusercontent.com/HromykoIvan/BirthdayHelper/master/deploy/ec2"

sudo curl -fsSL "$RAW/fetch-secrets.sh" -o "$BIN/fetch-secrets.sh"
sudo curl -fsSL "$RAW/update-duckdns.sh" -o "$BIN/update-duckdns.sh"
sudo curl -fsSL "$RAW/docker-compose.yml" -o "$ROOT/docker-compose.yml"
sudo curl -fsSL "$RAW/Caddyfile" -o "$ROOT/Caddyfile"

sudo chmod +x "$BIN/fetch-secrets.sh" "$BIN/update-duckdns.sh"

# systemd: сервис docker-compose
sudo tee /etc/systemd/system/birthday.service >/dev/null <<'UNIT'
[Unit]
Description=Birthday Bot Stack (Caddy + API)
After=network-online.target docker.service
Wants=network-online.target docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
WorkingDirectory=/opt/birthday
ExecStartPre=/usr/local/bin/fetch-secrets.sh
ExecStart=/usr/bin/docker compose -f /opt/birthday/docker-compose.yml up -d
ExecStop=/usr/bin/docker compose -f /opt/birthday/docker-compose.yml down
TimeoutStopSec=60
SuccessExitStatus=1
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
UNIT

# systemd: таймер DuckDNS (каждые 5 минут)
sudo tee /etc/systemd/system/duckdns.service >/dev/null <<'UNIT'
[Unit]
Description=DuckDNS IP updater

[Service]
Type=oneshot
ExecStart=/usr/local/bin/update-duckdns.sh
UNIT

sudo tee /etc/systemd/system/duckdns.timer >/dev/null <<'UNIT'
[Unit]
Description=Run DuckDNS updater every 5 minutes

[Timer]
OnBootSec=2min
OnUnitActiveSec=5min
Unit=duckdns.service

[Install]
WantedBy=timers.target
UNIT

sudo systemctl daemon-reload
sudo systemctl enable --now birthday.service duckdns.timer
