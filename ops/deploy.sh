#!/usr/bin/env bash
set -euo pipefail
cd /opt/birthday

# 1) подтянуть секреты в .env
REGION="${REGION:-eu-central-1}" 
DOMAIN="${DOMAIN:-bday-bot.duckdns.org}"
bash ./ops/env-from-secrets.sh

# 2) залогиниться в ECR
ACC_ID="$(aws sts get-caller-identity --query 'Account' --output text)"
ECR_REPO="${ECR_REPO:-birthday-helper}"

# Use sudo if not in docker group
if groups | grep -q docker; then
  DOCKER_CMD="docker"
else
  DOCKER_CMD="sudo docker"
fi

aws ecr get-login-password --region "$REGION" \
  | $DOCKER_CMD login --username AWS --password-stdin "${ACC_ID}.dkr.ecr.${REGION}.amazonaws.com"

# 3) создаем .compose.env для docker-compose
cat > .compose.env <<EOF
ACC_ID=${ACC_ID}
REGION=${REGION}
ECR_REPO=${ECR_REPO}
DOMAIN=${DOMAIN}
ACME_EMAIL=admin@example.com
EOF

# 4) подтянуть свежий образ и перезапустить
# Use sudo if not in docker group
if groups | grep -q docker; then
  DOCKER_CMD="docker"
else
  DOCKER_CMD="sudo docker"
fi

$DOCKER_CMD compose --env-file .compose.env pull app
$DOCKER_CMD compose --env-file .compose.env up -d
$DOCKER_CMD image prune -f || true

echo "[ok] deploy done"

