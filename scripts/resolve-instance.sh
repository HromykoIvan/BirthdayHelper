#!/usr/bin/env bash
# Resolves Bot EC2 Instance ID from SSM parameter or EC2 tag
set -euo pipefail

REGION="${AWS_REGION:-eu-central-1}"
PARAM="/birthday-bot/instance-id"

# Try SSM parameter first
if aws ssm get-parameter --name "$PARAM" --region "$REGION" >/dev/null 2>&1; then
  IID=$(aws ssm get-parameter --name "$PARAM" --region "$REGION" --query 'Parameter.Value' --output text)
  echo "Found in SSM: $IID" >&2
else
  # Fallback to EC2 tag
  echo "SSM parameter not found, searching by tag..." >&2
  IID=$(aws ec2 describe-instances \
    --region "$REGION" \
    --filters "Name=tag:Name,Values=BirthdayBotStack/BotInstance" "Name=instance-state-name,Values=running" \
    --query 'Reservations[0].Instances[0].InstanceId' --output text)
  echo "Found by tag: $IID" >&2
fi

if [ -z "$IID" ] || [ "$IID" = "None" ]; then
  echo "ERROR: Instance not found" >&2
  exit 1
fi

echo "$IID"

