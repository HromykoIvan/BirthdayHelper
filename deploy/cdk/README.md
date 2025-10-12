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

1. **Security Group** - разрешает HTTP/HTTPS трафик (порт 80 для ACME challenge, 443 для webhook)
2. **IAM Role** - с правами на ECR, SSM и CloudWatch
3. **EC2 Instance** - с предустановленным Docker и Caddy
4. **User Data Script** - автоматически:
   - Устанавливает Docker и Caddy
   - Логинится в ECR
   - Получает секреты из SSM
   - Запускает контейнер Birthday Bot
   - Настраивает Caddy для HTTPS

## Мониторинг

После деплоя CDK выведет:
- `PublicIp` - публичный IP инстанса
- `InstanceId` - ID инстанса для AWS Console

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

## Логи

```bash
# Логи Birthday Bot
sudo journalctl -u birthday-bot -f

# Логи Caddy
sudo journalctl -u caddy -f

# Логи Docker
sudo journalctl -u docker -f
```

## Troubleshooting

### Проверка статуса сервисов

```bash
sudo systemctl status birthday-bot
sudo systemctl status caddy
sudo systemctl status docker
```

### Перезапуск сервисов

```bash
sudo systemctl restart birthday-bot
sudo systemctl restart caddy
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
