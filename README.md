# BirthdayBot

BirthdayBot is a .NET 8 Telegram bot that sends birthday reminders and supports interactive management of users and birthdays.  
The project includes a production-ready deployment path on AWS with Docker, Caddy, CDK, and GitHub Actions.

## What This Repository Contains

- Clean Architecture backend (`backend/src/BirthdayBot.Api`)
- Telegram webhook endpoint (Minimal API)
- Reminder background service (cron-like checks every minute)
- MongoDB integration
- Docker-based runtime (`docker-compose.yml`)
- AWS CDK infrastructure (`deploy/cdk`)
- GitHub Actions CI/CD (`.github/workflows/build-and-push.yml`)

## Current Production Architecture

The infrastructure is optimized for low cost while avoiding full Spot-only risk:

- Auto Scaling Group with capacity `1` instance
- Mixed Instances policy:
  - Spot-first
  - On-Demand fallback when Spot is unavailable
- ARM64 instance family (`t4g.*`)
- Docker Compose runtime on EC2
- Caddy as reverse proxy + HTTPS termination
- Secrets pulled from AWS Secrets Manager at deploy/runtime
- Rollout via AWS SSM RunCommand from GitHub Actions

Important: with desired capacity `1`, instance replacement can still cause short interruptions while a new node boots.

## Prerequisites

- .NET 8 SDK
- Docker + Docker Compose
- Node.js 20+ (for CDK)
- AWS account with required IAM permissions
- Telegram bot token from BotFather

## Local Development

1. Create `.env` from your template and fill required variables.
2. Start services:

```bash
docker compose up -d --build
```

3. Verify local health:

```bash
curl http://localhost:8080/health/ready
```

4. Register webhook (if exposing publicly):

```bash
curl -X POST "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook" \
  -H "Content-Type: application/json" \
  -d "{\"url\":\"https://${PUBLIC_DOMAIN}/telegram/webhook\",\"secret_token\":\"${TELEGRAM_WEBHOOK_SECRET}\"}"
```

## AWS Deployment (CDK + GitHub Actions)

### 1) Configure GitHub repository variables

Required variables:

- `AWS_REGION`
- `AWS_ACCOUNT_ID`
- `AWS_ROLE_TO_ASSUME` (OIDC role ARN)
- `DOMAIN_NAME`
- `ECR_REPO`

### 2) Create required AWS secrets

Minimum:

- `birthday-bot/telegram-token`
- `birthday-bot/mongo-url`
- `birthday-bot/webhook-secret` (optional but recommended)
- `birthday-bot/duckdns-token` (optional)

You can also use unified secret `birthday-bot/all-config` if your workflow/scripts support it.

### 3) Bootstrap and deploy CDK

```bash
cd deploy/cdk
npm ci
npm run deploy
```

### 4) CI/CD flow

On push to `master` (for configured paths), the pipeline:

1. Runs tests
2. Builds ARM64 Docker image
3. Pushes image to ECR
4. Resolves active EC2 instance from ASG/SSM/tag
5. Executes rollout via SSM:
   - sync compose + caddy config
   - refresh `.env` from Secrets Manager
   - pull and restart containers
   - run health verification

## Runtime and Operations

### Health endpoints

- `/health/live`
- `/health/ready`
- `/health/startup`
- `/healthz`

### Metrics UI (cheap setup)

- Grafana is available at `/grafana` (proxied by Caddy).
- Prometheus scrapes metrics from the app on the internal Docker network.
- Public `/metrics` is blocked by Caddy; scrape uses `app:8080/metrics` (or `api:8080/metrics` on EC2 compose).

For local runtime:

```bash
docker compose up -d
open http://localhost/grafana
```

Default Grafana credentials can be set via:

- `GRAFANA_ADMIN_USER`
- `GRAFANA_ADMIN_PASSWORD`

### Local AI mode (same instance)

The bot supports a local-first AI path:

- free-text intent parsing (`LocalIntentRouter`)
- personalized greeting draft using birthday metadata from MongoDB
- optional Ollama-based rewrite on the same host

Config keys:

- `LocalAi:Enable`
- `LocalAi:UseOllama`
- `LocalAi:BaseUrl`
- `LocalAi:Model`
- `LocalAi:TimeoutSeconds`

If `UseOllama=false`, the bot still uses local deterministic enhancement (no external API calls).

To run Ollama in Docker compose:

```bash
docker compose --profile ai up -d ollama
```

In EC2 deployment compose, Ollama is started by default during rollout so `UseOllama=true` can work without a separate manual step.

The reminder flow adds an inline button to improve generated greeting text using the local AI enhancer.

You can also run ad-hoc greeting tests in chat with free text, for example:

- `сгенерируй поздравление для Сергей Калугин на 23 февраля`

In the bot UI, use the `🧪 Test greeting` button and then `🔄 Regenerate` to request another variant quickly.

For birthday reminders, the bot also provides:

- `🔄 Regenerate` to produce another variant
- `💬 Add comment` to give direct style instructions
- `✅ Use as example` to save the final text as preferred style for future generations

### AI memory and eval loop

AI runtime events are persisted to MongoDB collection `ai_events`:

- intent parse events (input phrase, predicted intent, confidence, prompt version, fallback flags)
- greeting enhancement events (draft text, output text, fallback reason, model source)

For iterative quality work:

- label expected intent on real phrases
- compute quality summary (overall and per prompt version)
- improve prompt version and compare again

Dev eval endpoints (enabled by `AiEval:EnableEndpoints`):

- `GET /api/ai/eval/unlabeled?take=50`
- `POST /api/ai/eval/label` with `{ "eventId": "...", "expectedIntent": "OpenList" }`
- `GET /api/ai/eval/summary?take=2000`

### Useful checks on EC2 (via SSM session)

```bash
cd /opt/birthday
docker compose ps
docker compose logs --tail=100 app
curl http://localhost:8080/health/ready
```

### Manual rollout helper

```bash
./scripts/rollout.sh latest
```

## Cost Notes

This setup is cost-optimized for small workloads:

- Spot-first ASG significantly lowers EC2 compute cost
- On-Demand fallback keeps deployment resilient to Spot shortages
- Single-instance topology minimizes baseline spend
- `gp3` root volume keeps storage cheap

For stricter high availability requirements (very low downtime), add a load balancer and at least 2 active instances.

## Security Notes

- GitHub Actions uses OIDC (no long-lived AWS keys)
- Secrets are fetched from Secrets Manager, not committed to git
- Webhook secret validation is enabled
- SSM is used for remote commands/session access

## Repository Layout

- `backend/` — application source and tests
- `deploy/cdk/` — infrastructure as code
- `ops/` — runtime scripts and service config
- `scripts/` — operational helper scripts
- `.github/workflows/` — CI/CD pipelines

## Troubleshooting

- **Deployment cannot find instance**  
  Check `/birthday-bot/bot-asg-name`, ASG health, and required EC2 tags.

- **Container is not healthy**  
  Review `docker compose logs app` and verify generated `.env`.

- **Webhook 401**  
  Ensure `Bot__WebhookSecretToken` matches Telegram webhook secret.

- **TLS not issuing**  
  Confirm DNS resolves to the current public IP and ports `80/443` are open.

- **No Grafana data**  
  Check `docker compose logs prometheus` and ensure app metrics are available on internal target (`app:8080/metrics` or `api:8080/metrics`).

- **Grafana URL returns 404**  
  Ensure rollout uses the latest Caddy/Compose config and starts `grafana` + `prometheus` services (not only `app` and `caddy`). For EC2 compose, Caddy must read `/opt/birthday/ops/caddy/Caddyfile`.

- **Local AI fallback too often**  
  Verify local model runtime availability and timeout settings in `LocalAi` options.

## License

MIT
