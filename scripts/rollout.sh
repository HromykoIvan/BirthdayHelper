#!/usr/bin/env bash
# Rollout Docker image to EC2 instance via SSM
set -euo pipefail

REGION="${AWS_REGION:-eu-central-1}"
ECR_REPO="${ECR_REPO:-birthday-helper}"
IMAGE="${1:-latest}"

# Resolve instance ID
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IID=$("$SCRIPT_DIR/resolve-instance.sh")

echo "Instance: $IID"
echo "Image: $IMAGE"

# Get AWS account ID
ACCOUNT_ID=$(aws sts get-caller-identity --query 'Account' --output text)
ECR_URI="${ACCOUNT_ID}.dkr.ecr.${REGION}.amazonaws.com/${ECR_REPO}"

# Full image URI
if [[ "$IMAGE" == *":"* ]]; then
  # Already has tag or full URI
  FULL_IMAGE="$IMAGE"
else
  # Add repo prefix and tag
  FULL_IMAGE="${ECR_URI}:${IMAGE}"
fi

echo "Full image URI: $FULL_IMAGE"
echo "Sending SSM command..."

COMMAND_ID=$(aws ssm send-command \
  --instance-ids "$IID" \
  --document-name "AWS-RunShellScript" \
  --comment "Rollout birthday-bot ${FULL_IMAGE}" \
  --parameters "commands=[
    \"set -e\",
    \"echo '[$(date)] Starting rollout...'\",
    \"aws ecr get-login-password --region ${REGION} | docker login --username AWS --password-stdin ${ECR_URI}\",
    \"cd /opt/birthday || exit 1\",
    \"docker pull ${FULL_IMAGE}\",
    \"docker compose --env-file .compose.env down app || true\",
    \"docker compose --env-file .compose.env up -d app\",
    \"docker image prune -f || true\",
    \"echo '[$(date)] Rollout completed'\"
  ]" \
  --region "$REGION" \
  --query "Command.CommandId" \
  --output text)

echo "Command ID: $COMMAND_ID"
echo "Waiting for completion..."

aws ssm wait command-executed \
  --command-id "$COMMAND_ID" \
  --instance-id "$IID" \
  --region "$REGION" || true

echo "✓ Rollout completed!"

