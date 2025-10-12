#!/usr/bin/env bash
set -euo pipefail
cd /opt/birthday

# 1) подтянуть секреты в .env
REGION="${REGION:-eu-central-1}" DOMAIN="${DOMAIN:-bday-bot.duckdns.org}" \
  bash ./ops/env-from-secrets.sh

# 2) залогиниться в ECR и подтянуть свежий образ
AWS_REGION="${REGION}"
ACC_ID="$(aws sts get-caller-identity --query 'Account' --output text)"
ECR_REPO="${ACC_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com/birthday-bot"

aws ecr get-login-password --region "$AWS_REGION" \
  | docker login --username AWS --password-stdin "${ACC_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com"

export ECR_REPO
docker compose pull app
docker compose up -d
docker image prune -f || true

echo "[ok] deploy done"

