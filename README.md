# BirthdayBot — Telegram напоминания о днях рождения

## Кратко
Рабочий шаблон на .NET 8 с Clean Architecture, MongoDB, Telegram webhook (Minimal API), фоновым сервисом (cron каждую минуту), i18n генератором поздравлений (RU/PL/EN), Docker/Compose, AWS CDK для деплоя на EC2, GitHub Actions CI/CD с OIDC, health-checks, Prometheus метрики и rate-limiting.

---

## Шаг-за-шагом

### Шаг 1. Локальный запуск через docker-compose
1. Установи Docker + Docker Compose.
2. Создай `.env` из `.env.example` и заполни:
   - `TELEGRAM_BOT_TOKEN` — токен твоего бота из @BotFather.
   - `TELEGRAM_WEBHOOK_SECRET` — произвольная строка для валидации вебхука.
   - `NGROK_AUTHTOKEN` и `NGROK_DOMAIN` — если хочешь быстрый публичный URL.
3. Запусти:
   ```bash
   docker-compose up -d --build
4. После старта получишь публичный URL от ngrok (https://<NGROK_DOMAIN>). Установи webhook:
    curl -X POST "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook" \
    -H "Content-Type: application/json" \
    -d "{\"url\":\"https://${NGROK_DOMAIN}/telegram/webhook\",\"secret_token\":\"${TELEGRAM_WEBHOOK_SECRET}\"}"
5. Напиши боту /start. Проверь health и метрики:
    curl http://localhost:8080/health/ready
    curl http://localhost:8080/metrics

### Шаг 2. Добавь запись в БД
    /add_birthday → бот спросит имя → дату YYYY-MM-DD → таймзону (Enter — по умолчанию Europe/Warsaw).
    /list — список с inline-кнопками удаления.
    /settings — отправь, например:
    09:30 или Europe/Warsaw или ru/pl/en или auto on/auto off или formal/friendly.

### Шаг 3. AWS CDK деплой
    Используется AWS CDK для автоматического развертывания на EC2 с Caddy reverse proxy.
    
    **Быстрый старт:**
    1. Настройте AWS OIDC (см. `deploy/cdk/SETUP.md`)
    2. Создайте GitHub Repository Variables
    3. Создайте SSM параметры с секретами
    4. Сделайте push в ветку `master` — автоматический деплой
    
    **Ручной деплой:**
    ```bash
    cd deploy/cdk
    npm install
    export DOMAIN_NAME="bot.example.com"
    export CDK_DEFAULT_ACCOUNT="123456789012"
    export CDK_DEFAULT_REGION="us-east-1"
    npm run deploy
    ```
    
    **Что создается:**
    - EC2 t4g.micro (ARM64) с Amazon Linux 2023
    - Docker контейнер с Birthday Bot
    - Caddy с автоматическим HTTPS
    - IAM роли для ECR и SSM доступа

    Переменные окружения (API)
    | Variable                  | Description                             | Example                                            |
    | ------------------------- | --------------------------------------- | -------------------------------------------------- |
    | `MONGODB_URI`             | строка подключения к MongoDB            | `mongodb://mongodb:27017/birthdays?replicaSet=rs0` |
    | `MONGO_DBNAME`            | имя БД                                  | `birthdays`                                        |
    | `TELEGRAM_BOT_TOKEN`      | токен бота                              | `123456:ABC...`                                    |
    | `TELEGRAM_WEBHOOK_SECRET` | секрет для валидации заголовка Telegram | `REPLACE_ME_WEBHOOK_SECRET`                        |
    | `ASPNETCORE_URLS`         | адреса Kestrel                          | `http://0.0.0.0:8080`                              |

   GitHub Repository Variables (для CDK деплоя)
    - AWS_ACCOUNT_ID — 123456789012
    - AWS_REGION — us-east-1
    - AWS_ROLE_TO_ASSUME — ARN роли OIDC для GitHub
    - DOMAIN_NAME — bot.example.com
    - ECR_REPO — birthday-helper

    Mongo индексы
        Индексы создаются автоматически при старте:
        Users: уникальный по TelegramUserId
        Birthdays: UserId, и нестрогий (UserId, Name) (включи уникальность при необходимости)
        DeliveryLog: UserId

    Наблюдаемость/Безопасность
        Health: /health/live, /health/ready, /health/startup
        Prometheus: /metrics (через OpenTelemetry exporter)
        Rate limiting webhook: фиксированное окно 60 req/min/IP
        NetworkPolicy: разрешает egress к DNS, Mongo и TCP/443 в интернет (для Telegram). У Telegram плавающие IP — точное ограничение по IP невозможно без egress-gateway

    Частые проблемы
        Webhook 401 — не совпадает X-Telegram-Bot-Api-Secret-Token. Проверь SSM параметр и что он совпадает с тем, что задавал в setWebhook.
        CDK Bootstrap ошибки — убедись, что OIDC роль имеет права на S3 и CloudFormation.
        EC2 недоступен — проверь Security Group, убедись что порты 80/443 открыты.
        Caddy не запускается — проверь DNS настройки домена.
        TZ — используй точные ID из tzdb (например, Europe/Warsaw). В /settings можно прислать любой валидный ID.
        Состояние диалогов — в MVP хранится в памяти. Для продакшен-масштабирования добавь Redis (stateful).
    
    AWS Secrets Manager
        Создайте секреты в AWS Secrets Manager:
        ```bash
        aws secretsmanager create-secret --name "birthday-bot/telegram-token" \
          --secret-string "YOUR_TOKEN"
        aws secretsmanager create-secret --name "birthday-bot/mongo-url" \
          --secret-string "mongodb://..."
        aws secretsmanager create-secret --name "birthday-bot/duckdns-token" \
          --secret-string "YOUR_DUCKDNS_TOKEN"
        ```

    Лицензия
        MIT