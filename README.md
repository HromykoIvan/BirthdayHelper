# BirthdayBot — Telegram напоминания о днях рождения

## Кратко
Рабочий шаблон на .NET 8 с Clean Architecture, MongoDB, Telegram webhook (Minimal API), фоновым сервисом (cron каждую минуту), i18n генератором поздравлений (RU/PL/EN), Docker/Compose, Kubernetes + Helm, GitHub Actions CI/CD в AWS ECR/EKS, health-checks, Prometheus метрики и rate-limiting.

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

### Шаг 3. Готовим AWS
    Минимальные terraform-ish заметки (всё можно и eksctl/консолью):
        ECR: aws ecr create-repository --repository-name birthday-bot
        EKS: кластер с managed node group + AWS Load Balancer Controller + NGINX Ingress Controller.
        IRSA для GitHub OIDC роли (или используй KUBE_CONFIG_B64):
        Создай IAM роль и доверь провайдеру GitHub (aws-actions/configure-aws-credentials), выдай ecr:* и eks:Describe* + доступ к kubectl через aws eks update-kubeconfig.

### Шаг 4. Helm-деплой в EKS
    Заполни секреты в GitHub (см. ниже).
    Запусти пайплайн — при push в main произойдёт сборка образа в ECR и helm upgrade --install.
    После деплоя:
        kubectl -n birthday-bot get ingress
    Возьми HOST и установи Telegram webhook:
        export TELEGRAM_BOT_TOKEN=...    # твой токен
        export TELEGRAM_WEBHOOK_SECRET=REPLACE_ME_WEBHOOK_SECRET
        export HOST=birthdays.example.com
        curl -X POST "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook" \
        -H "Content-Type: application/json" \
        -d "{\"url\":\"https://${HOST}/telegram/webhook\",\"secret_token\":\"${TELEGRAM_WEBHOOK_SECRET}\"}"

    Переменные окружения (API)
    | Variable                  | Description                             | Example                                            |
    | ------------------------- | --------------------------------------- | -------------------------------------------------- |
    | `MONGODB_URI`             | строка подключения к MongoDB            | `mongodb://mongodb:27017/birthdays?replicaSet=rs0` |
    | `MONGO_DBNAME`            | имя БД                                  | `birthdays`                                        |
    | `TELEGRAM_BOT_TOKEN`      | токен бота                              | `123456:ABC...`                                    |
    | `TELEGRAM_WEBHOOK_SECRET` | секрет для валидации заголовка Telegram | `REPLACE_ME_WEBHOOK_SECRET`                        |
    | `ASPNETCORE_URLS`         | адреса Kestrel                          | `http://0.0.0.0:8080`                              |

   GitHub Secrets 
    - AWS_ACCOUNT_ID — 123456789012
    - AWS_REGION — eu-central-1
    - ECR_REPO — birthday-bot
    - AWS_ROLE_TO_ASSUME — ARN роли OIDC для GitHub (альтернатива — KUBE_CONFIG_B64)
    - KUBE_CONFIG_B64 — base64 kubeconfig (если не используешь OIDC)
    - DOMAIN_NAME — birthdays.example.com
    - STORAGE_CLASS — gp3
    - (опционально) TELEGRAM_BOT_TOKEN — если хочешь прокидывать секрет в K8s прямо из Actions (в моём чарте уже есть Secret из base64 примеров — меняй в values.yaml).

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
        Webhook 401 — не совпадает X-Telegram-Bot-Api-Secret-Token. Проверь секрет и что он совпадает с тем, что задавал в setWebhook.
        Timeout 502/504 — проверь NGINX Ingress аннотации, readiness/liveness. Убедись, что /health/ready отдаёт 200.
        TZ — используй точные ID из tzdb (например, Europe/Warsaw). В /settings можно прислать любой валидный ID.
        Состояние диалогов — в MVP хранится в памяти. Для продакшен-масштабирования добавь Redis (stateful).
    
    Как поменять значения Secret (base64)
        В манифесте deploy/k8s/secret-telegram.yaml уже валидные base64. Чтобы заменить:
        echo -n 'YOUR_REAL_TOKEN' | base64
        Подставь значение в поле TELEGRAM_BOT_TOKEN. То же для MONGODB_URI и TELEGRAM_WEBHOOK_SECRET.

    Лицензия
        MIT