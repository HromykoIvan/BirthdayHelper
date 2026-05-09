# Deployment Documentation

## 📋 Overview

This document describes the current working deployment setup for BirthdayBot. The deployment is fully automated via GitHub Actions and uses AWS EC2, ECR, SSM, and Docker Compose.

**Current Status:** ✅ Production-ready, stable deployment pipeline

---

## 🏗️ Architecture

### Components

1. **GitHub Actions CI/CD Pipeline** (`.github/workflows/build-and-push.yml`)
   - Builds Docker image for `linux/arm64` platform
   - Pushes to AWS ECR
   - Deploys to EC2 via SSM

2. **AWS Infrastructure**
   - **EC2 Instance** (ARM64, Amazon Linux 2023)
     - Bot application container
     - MongoDB container
     - Caddy reverse proxy (HTTPS termination)
   - **ECR Repository** - Docker image storage
   - **SSM Parameter Store** - Instance ID storage
   - **Secrets Manager** - Sensitive configuration

3. **Docker Compose** (`deploy/ec2/docker-compose.yml`)
   - `api` service - BirthdayBot application
   - `caddy` service - Reverse proxy with automatic HTTPS

---

## 🔄 Deployment Flow

### Automatic Deployment (GitHub Actions)

**Trigger:** Push to `master` branch (changes in `backend/**`, `Dockerfile`, `.github/workflows/build-and-push.yml`, `ops/caddy/**`, `deploy/ec2/**`)

**Process:**

1. **Build Job** (`build-and-push`)
   - Authenticates to AWS via OIDC
   - Ensures ECR repository exists
   - Builds Docker image with `docker buildx` for ARM64
   - Tags image with commit SHA and `latest`
   - Pushes to ECR

2. **Rollout Job** (`rollout`)
   - Resolves EC2 instance ID from SSM parameter or EC2 tags
   - Sends SSM command to instance:
     - Logs into ECR
     - Pulls new image
     - Stops old container
     - Starts new container with `docker compose`
     - Normalizes environment variables
   - Waits for command completion (max 5 minutes)
   - Verifies container health via HTTP health check

3. **Health Check** (`verify container health`)
   - Polls `/health/ready` endpoint (up to 18 attempts, 5s interval)
   - Checks container status
   - Displays logs if verification fails

---

## 🔧 Configuration

### GitHub Repository Variables

Required variables in GitHub repository settings:

| Variable | Description | Example |
|----------|-------------|---------|
| `AWS_REGION` | AWS region | `eu-central-1` |
| `AWS_ACCOUNT_ID` | AWS account ID | `123456789012` |
| `AWS_ROLE_TO_ASSUME` | OIDC role ARN for GitHub Actions | `arn:aws:iam::123456789012:role/github-actions-role` |
| `ECR_REPO` | ECR repository name | `birthday-helper` |

### AWS SSM Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `/birthday-bot/bot-instance-id` | EC2 instance ID (optional, can use tags) | `i-0f84acabd8293b119` |

### AWS Secrets Manager

| Secret Name | Description | Example |
|-------------|-------------|---------|
| `birthday-bot/telegram-token` | Telegram bot token | `123456:ABC...` |
| `birthday-bot/webhook-secret` | Webhook secret token | `REPLACE_ME_WEBHOOK_SECRET` |
| `birthday-bot/mongo-url` | MongoDB connection string | `mongodb://mongodb:27017/birthdays?replicaSet=rs0` |
| `birthday-bot/duckdns-token` | DuckDNS token (if using) | `YOUR_DUCKDNS_TOKEN` |

### Environment Variables on EC2

The deployment script normalizes environment variables in `/opt/birthday/.env`:

- `BOT__TOKEN` - Telegram bot token
- `BOT__WEBHOOKSECRET` - Webhook secret
- `BOT__WEBHOOKSECRETTOKEN` - Webhook secret (alias)
- `MONGODB_URI` - MongoDB connection string
- `MONGO_DBNAME` - Database name

---

## 📦 Docker Compose Configuration

### Location
`/opt/birthday/docker-compose.yml` on EC2 instance

### Services

#### `api` Service
- **Image:** From ECR (tagged with commit SHA or `latest`)
- **Port:** `8080` (internal)
- **Environment:** Loaded from `/opt/birthday/.env`
- **Restart:** `unless-stopped`

#### `caddy` Service
- **Image:** `caddy:2`
- **Ports:** `80:80`, `443:443` (exposed)
- **Volumes:**
  - `/opt/birthday/Caddyfile` - Caddy configuration
  - `caddy_data` - SSL certificates
  - `caddy_config` - Caddy config
- **Depends on:** `api`
- **Restart:** `unless-stopped`

---

## 🚀 Manual Deployment

If you need to deploy manually:

### 1. Build and Push Image

```bash
# Set variables
export AWS_REGION=eu-central-1
export AWS_ACCOUNT_ID=123456789012
export ECR_REPO=birthday-helper
export IMAGE_TAG=$(git rev-parse HEAD)

# Login to ECR
aws ecr get-login-password --region $AWS_REGION | \
  docker login --username AWS --password-stdin \
  $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com

# Build and push
docker buildx build \
  --platform linux/arm64 \
  -t $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO:$IMAGE_TAG \
  -t $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO:latest \
  --push \
  .
```

### 2. Deploy to EC2

```bash
# Get instance ID
INSTANCE_ID=$(aws ec2 describe-instances \
  --region eu-central-1 \
  --filters "Name=tag:Name,Values=BirthdayBotStack/BotInstance" \
            "Name=instance-state-name,Values=running" \
  --query 'Reservations[0].Instances[0].InstanceId' \
  --output text)

# Send deployment command via SSM
aws ssm send-command \
  --instance-ids $INSTANCE_ID \
  --document-name "AWS-RunShellScript" \
  --parameters '{"commands":["cd /opt/birthday","docker compose pull","docker compose up -d app"]}' \
  --region eu-central-1
```

---

## 🔍 Monitoring & Troubleshooting

### Check Deployment Status

```bash
# View GitHub Actions workflow runs
# Or check SSM command status:
aws ssm list-command-invocations \
  --instance-id i-0f84acabd8293b119 \
  --region eu-central-1 \
  --max-items 5
```

### View Container Logs

```bash
# Connect to EC2 via SSM
aws ssm start-session --target i-0f84acabd8293b119 --region eu-central-1

# In the session:
cd /opt/birthday
docker compose logs -f app
```

### Health Check

```bash
# From local machine (if domain is accessible)
curl https://your-domain.com/health/ready

# From EC2 instance
curl http://localhost:8080/health/ready
```

### Common Issues

#### 1. Deployment Fails: "Instance not found"
- **Solution:** Check SSM parameter `/birthday-bot/bot-instance-id` or EC2 tags
- **Verify:** `aws ec2 describe-instances --filters "Name=tag:Name,Values=BirthdayBotStack/BotInstance"`

#### 2. Container Not Starting
- **Check logs:** `docker compose logs app`
- **Check environment:** `cat /opt/birthday/.env`
- **Verify secrets:** Ensure Secrets Manager values are correct

#### 3. Health Check Fails
- **Check container status:** `docker compose ps`
- **Check application logs:** `docker compose logs app --tail=100`
- **Verify port:** Ensure port 8080 is accessible internally

#### 4. Webhook 401 Unauthorized
- **Verify secret:** Check `BOT__WEBHOOKSECRET` in `.env` matches Telegram webhook secret
- **Check header:** Telegram must send `X-Telegram-Bot-Api-Secret-Token` header

---

## 🔐 Security

### Current Security Measures

1. **OIDC Authentication** - GitHub Actions uses OIDC instead of static credentials
2. **SSM Port Forwarding** - MongoDB access via SSM (no public ports)
3. **Webhook Secret Validation** - All webhook requests validated
4. **Secrets Manager** - Sensitive data stored securely
5. **IAM Roles** - Least privilege access

### Best Practices

- ✅ Never commit secrets to git
- ✅ Use IAM roles instead of access keys
- ✅ Rotate webhook secrets periodically
- ✅ Monitor SSM command history
- ✅ Review CloudTrail logs for access

---

## 📝 Telegram Webhook Interaction

### How It Works

1. **Webhook Setup**
   ```bash
   curl -X POST "https://api.telegram.org/bot$TOKEN/setWebhook" \
     -H "Content-Type: application/json" \
     -d '{"url":"https://your-domain.com/telegram/webhook","secret_token":"YOUR_SECRET"}'
   ```

2. **Request Flow**
   - Telegram sends POST to `/telegram/webhook`
   - Caddy terminates HTTPS and forwards to `api:8080`
   - `TelegramEndpoints.cs` validates `X-Telegram-Bot-Api-Secret-Token` header
   - `UpdateHandler` processes the update
   - Returns `200 OK` to Telegram

3. **Key Components**
   - **Endpoint:** `POST /telegram/webhook` (Minimal API)
   - **Validation:** Webhook secret token in header
   - **Handler:** `UpdateHandler.HandleUpdateAsync()`
   - **Response:** Always `200 OK` (errors handled internally)

### Current Implementation Status

✅ **Working Features:**
- Webhook secret validation
- Update deserialization
- Error handling with logging
- Callback query handling
- Message command processing
- Wizard flow for adding birthdays

---

## 🔄 Rollback Procedure

If deployment fails, rollback to previous version:

```bash
# Get previous image tag from ECR
aws ecr describe-images \
  --repository-name birthday-helper \
  --region eu-central-1 \
  --query 'sort_by(imageDetails,&imagePushedAt)[-2].imageTags[0]' \
  --output text

# Deploy previous image
# (Use the deployment command with specific tag)
```

Or manually:

```bash
# On EC2 instance
cd /opt/birthday
docker compose pull app
docker compose up -d app
```

---

## 📊 Deployment Checklist

Before deploying:

- [ ] GitHub repository variables configured
- [ ] AWS OIDC role has necessary permissions
- [ ] ECR repository exists
- [ ] SSM parameter or EC2 tags configured
- [ ] Secrets Manager secrets created
- [ ] Domain DNS configured (for Caddy)
- [ ] Telegram webhook configured with correct secret

After deployment:

- [ ] Container is running (`docker compose ps`)
- [ ] Health check passes (`/health/ready`)
- [ ] Webhook responds correctly
- [ ] Test bot interaction (`/start` command)
- [ ] Check application logs for errors

---

## 📚 Related Documentation

- **[MONGODB_COMPASS_SETUP.md](MONGODB_COMPASS_SETUP.md)** - How to view database
- **[WEBHOOK_SECRET_EXPLANATION.md](WEBHOOK_SECRET_EXPLANATION.md)** - Webhook security
- **[README.md](README.md)** - General project overview
- **[LOCAL_TESTING.md](LOCAL_TESTING.md)** - Local development setup

---

## 🎯 Quick Reference

### Deployment Commands

```bash
# Automatic (via GitHub Actions)
git push origin master

# Manual build
docker buildx build --platform linux/arm64 -t <image> --push .

# Manual deploy
aws ssm send-command --instance-ids <id> --document-name "AWS-RunShellScript" ...
```

### Monitoring Commands

```bash
# Check container status
aws ssm start-session --target <instance-id>
docker compose ps

# View logs
docker compose logs -f app

# Health check
curl http://localhost:8080/health/ready
```

---

**Last Updated:** Based on current working version (GitHub Actions workflow + SSM deployment)
