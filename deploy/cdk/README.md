# Birthday Bot CDK Infrastructure

Этот CDK стек развертывает Birthday Bot на AWS EC2 с автоматическим управлением через Caddy reverse proxy.

## Архитектура

- **EC2 Instance**: t4g.micro (ARM64) с Amazon Linux 2023
- **Docker**: Контейнер с Birthday Bot приложением
- **Caddy**: Автоматический HTTPS reverse proxy
- **SSM Parameters**: Хранение секретов (Bot Token, MongoDB URI)
- **IAM Role**: Доступ к ECR и SSM

## Предварительные требования

1. AWS CLI настроен с соответствующими правами
2. CDK CLI установлен: `npm install -g aws-cdk`
3. Node.js 18+ для сборки CDK
4. ECR репозиторий создан для образа приложения

## Настройка

### 1. Установка зависимостей

```bash
cd deploy/cdk
npm install
```

### 2. Создание SSM параметров

```bash
# Bot Token
aws ssm put-parameter \
  --name "/birthday-bot/BOT_TOKEN" \
  --value "YOUR_TELEGRAM_BOT_TOKEN" \
  --type "SecureString"

# Webhook Secret (опционально)
aws ssm put-parameter \
  --name "/birthday-bot/WEBHOOK_SECRET" \
  --value "YOUR_WEBHOOK_SECRET" \
  --type "SecureString"

# MongoDB URI
aws ssm put-parameter \
  --name "/birthday-bot/MONGODB_URI" \
  --value "mongodb://your-mongo-connection-string" \
  --type "SecureString"
```

### 3. Переменные окружения

```bash
export DOMAIN_NAME="your-domain.com"
export ECR_REPO="birthday-helper"
export IMAGE_TAG="latest"
export CDK_DEFAULT_ACCOUNT="123456789012"
export CDK_DEFAULT_REGION="us-east-1"
```

## Деплой

### Синтез шаблона

```bash
npm run synth
```

### Деплой стека

```bash
npm run deploy
```

### Уничтожение стека

```bash
npm run destroy
```

## Что создается

1. **Security Groups** - Bot SG (HTTP/HTTPS) и Mongo SG (27017 только от Bot SG)
2. **IAM Roles** - с правами на ECR, Secrets Manager и CloudWatch
3. **EC2 Instances** - Bot Instance (API + Caddy) и Mongo Instance (MongoDB 6)
4. **Private DNS** - Route53 приватная зона `svc.local` с записью `mongo.svc.local`
5. **Автоматизация** - система скриптов:
   - Получение секретов из AWS Secrets Manager
   - Автоматическое обновление IP в DuckDNS
   - Docker Compose для запуска API + Caddy
   - Автоматический HTTPS через Let's Encrypt
   - Автоматическое обновление MongoDB connection string

## Мониторинг

После деплоя CDK выведет:
- `PublicIp` - публичный IP инстанса бота
- `InstanceId` - ID инстанса бота для мониторинга
- `MongoInstanceId` - ID MongoDB инстанса
- `MongoPrivateIp` - приватный IP MongoDB

## Проверка доступности

### С внешней сети (телефон LTE):
```bash
# Проверка HTTP (должен отвечать 200/301/403)
curl -I http://your-domain.com

# Проверка HTTPS (после установки TLS)
curl -kI https://your-domain.com
```

### С EC2 инстанса:
```bash
# Проверка слушающих портов
sudo ss -lntp | egrep ':80|:443'
```

### Проверка MongoDB:
```bash
# Подключение к MongoDB через SSM Session Manager
aws ssm start-session --target i-<MongoInstanceId>

# На MongoDB инстансе:
sudo docker logs mongo                    # логи MongoDB
sudo docker exec -it mongo mongosh        # подключение к MongoDB
```

## Логи

```bash
# Логи Birthday Bot сервиса (Docker Compose)
sudo journalctl -u birthday -f

# Логи DuckDNS обновления
sudo journalctl -u duckdns -f

# Логи Docker контейнеров
sudo docker logs birthday_api_1

# Логи MongoDB (через SSM)
aws ssm start-session --target i-<MongoInstanceId>
sudo docker logs -f mongo
sudo docker logs birthday_caddy_1

# Логи Docker
sudo journalctl -u docker -f
```

## Troubleshooting

### Проверка статуса сервисов

```bash
sudo systemctl status birthday
sudo systemctl status duckdns.timer
sudo systemctl status docker
sudo docker ps
```

### Перезапуск сервисов

```bash
sudo systemctl restart birthday
sudo systemctl restart duckdns.timer
```

### Проверка конфигурации Caddy

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
```

### Проверка Docker контейнера

```bash
sudo docker ps
sudo docker logs birthday-bot
```

## Обновление приложения

1. Соберите новый образ и отправьте в ECR
2. Обновите `IMAGE_TAG` переменную окружения
3. Перезапустите инстанс или выполните:

```bash
sudo systemctl restart birthday-bot
```
