# CDK Setup Guide

Этот документ описывает настройку AWS CDK для автоматического деплоя Birthday Bot через GitHub Actions.

## Архитектура

1. **EC2 Instances** (2x t4g.micro, Free Tier):
   - **Bot Instance** - основной сервер с API и Caddy
   - **Mongo Instance** - MongoDB 6 в Docker контейнере
2. **Security Groups**:
   - **Bot SG** - разрешает HTTP/HTTPS (порт 80 для ACME, 443 для webhook)
   - **Mongo SG** - разрешает MongoDB (порт 27017 только от Bot SG)
3. **Private DNS** - Route53 приватная зона `svc.local` с записью `mongo.svc.local`
4. **IAM Roles** - права на ECR, Secrets Manager, CloudWatch, SSM
5. **Автоматизация** - Docker Compose, обновление IP, HTTPS, MongoDB connection string

## 1. AWS OIDC настройка

### Создание OIDC Identity Provider

1. Откройте AWS IAM Console
2. Перейдите в **Identity providers** → **Add provider**
3. Выберите **OpenID Connect**
4. Укажите:
   - **Provider URL**: `https://token.actions.githubusercontent.com`
   - **Audience**: `sts.amazonaws.com`
5. Нажмите **Add provider**

### Создание IAM Role для GitHub Actions

1. В IAM Console перейдите в **Roles** → **Create role**
2. Выберите **Web identity**
3. **Identity provider**: `token.actions.githubusercontent.com`
4. **Audience**: `sts.amazonaws.com`
5. Нажмите **Next**

### Trust Policy

Добавьте следующую Trust Policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::YOUR_ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": "repo:YOUR_USERNAME/YOUR_REPO:*"
        }
      }
    }
  ]
}
```

### Permissions Policy

Прикрепите следующие managed policies:
- `AmazonEC2FullAccess`
- `IAMFullAccess`
- `AmazonSSMFullAccess`
- `AmazonEC2ContainerRegistryFullAccess`
- `CloudFormationFullAccess`
- `AmazonS3FullAccess`

## Безопасность Security Group

CDK автоматически создает Security Group с правильными правилами:
- **Порт 80** - для ACME challenge от Let's Encrypt
- **Порт 443** - для HTTPS webhook от Telegram
- **IPv6 поддержка** - для будущей совместимости
- **Outbound** - все трафик разрешен (нужно для Let's Encrypt и Telegram)

**Важно:** Порт 80 оставляется открытым для ACME проверки и редиректа на 443. Это стандартная практика для Let's Encrypt.

## 2. GitHub Repository Variables

В настройках репозитория GitHub добавьте следующие **Repository variables**:

### Required Variables

| Variable Name | Description | Example |
|---------------|-------------|---------|
| `AWS_REGION` | AWS регион для деплоя | `us-east-1` |
| `AWS_ACCOUNT_ID` | ID вашего AWS аккаунта | `123456789012` |
| `AWS_ROLE_TO_ASSUME` | ARN OIDC роли | `arn:aws:iam::123456789012:role/github-actions-role` |
| `DOMAIN_NAME` | Домен для бота | `bot.example.com` |
| `ECR_REPO` | Имя ECR репозитория | `birthday-helper` |

### Как добавить переменные:

1. Перейдите в **Settings** вашего GitHub репозитория
2. В левом меню выберите **Secrets and variables** → **Actions**
3. Перейдите на вкладку **Variables**
4. Нажмите **New repository variable**
5. Добавьте каждую переменную из таблицы выше

## 3. AWS Secrets Manager

Создайте следующие секреты в AWS Secrets Manager:

```bash
# Telegram Bot Token
aws secretsmanager create-secret \
  --name "birthday-bot/telegram-token" \
  --description "Telegram Bot Token" \
  --secret-string "YOUR_TELEGRAM_BOT_TOKEN"

# MongoDB Connection String
aws secretsmanager create-secret \
  --name "birthday-bot/mongo-url" \
  --description "MongoDB Connection URL" \
  --secret-string "mongodb://your-mongo-connection-string"

# DuckDNS Token (для автоматического обновления IP)
aws secretsmanager create-secret \
  --name "birthday-bot/duckdns-token" \
  --description "DuckDNS Token for IP updates" \
  --secret-string "YOUR_DUCKDNS_TOKEN"
```

**Примечание:** CDK автоматически дает EC2 права на чтение всех секретов с префиксом `birthday-bot/*` для максимальной безопасности.

## 4. ECR Repository

Создайте ECR репозиторий для Docker образов:

```bash
aws ecr create-repository --repository-name birthday-helper --region us-east-1
```

## 5. Домен и DNS

1. Настройте DNS запись для вашего домена:
   - **Type**: A record
   - **Name**: `bot` (или что вы указали в `DOMAIN_NAME`)
   - **Value**: Будет получен после деплоя (PublicIp из CDK output)

2. После первого деплоя обновите DNS запись с полученным PublicIp

## 6. Первый деплой

### Автоматический деплой

1. Сделайте push в ветку `master` с изменениями в `deploy/cdk/`
2. Или запустите workflow вручную через GitHub Actions

**Примечание:** CDK bootstrap выполняется автоматически только при первом деплое. GitHub Actions проверяет существование стека `CDKToolkit` и запускает bootstrap только если его нет, что ускоряет повторные деплои.

### Ручной деплой (для тестирования)

```bash
cd deploy/cdk
npm install
export DOMAIN_NAME="bot.example.com"
export CDK_DEFAULT_ACCOUNT="123456789012"
export CDK_DEFAULT_REGION="us-east-1"
export ECR_REPO="birthday-helper"
npm run deploy
```

## 7. MongoDB автоматизация

После деплоя CDK автоматически:

1. **Создает MongoDB инстанс** с Docker контейнером `mongo:6`
2. **Обновляет секрет** `birthday-bot/mongo-url` на `mongodb://mongo.svc.local:27017/birthdaybot`
3. **Настраивает приватный DNS** - `mongo.svc.local` → приватный IP MongoDB
4. **Применяет безопасность** - MongoDB доступен только с Bot SG

### Проверка MongoDB

```bash
# Подключение к MongoDB через SSM
aws ssm start-session --target i-<MongoInstanceId>

# На MongoDB инстансе:
sudo docker logs mongo                    # логи MongoDB
sudo docker exec -it mongo mongosh        # подключение к MongoDB
```

## 8. Мониторинг деплоя

### GitHub Actions

- Перейдите в **Actions** в вашем GitHub репозитории
- Найдите workflow "CDK Deploy (EC2)"
- Проверьте логи выполнения

### AWS Console

- **CloudFormation**: Проверьте стек "BirthdayBotStack"
- **EC2**: Найдите созданный инстанс
- **SSM**: Проверьте параметры

### Логи приложения

После успешного деплоя подключитесь к EC2 инстансу:

```bash
# Через SSM Session Manager
aws ssm start-session --target i-1234567890abcdef0

# Логи Birthday Bot
sudo journalctl -u birthday-bot -f

# Логи Caddy
sudo journalctl -u caddy -f
```

## 8. Troubleshooting

### Частые проблемы:

1. **CDK Bootstrap ошибки**: Убедитесь, что OIDC роль имеет права на S3 и CloudFormation
2. **ECR доступ**: Проверьте, что роль может читать ECR репозиторий
3. **SSM параметры**: Убедитесь, что все параметры созданы и роль имеет к ним доступ
4. **DNS**: Проверьте, что домен указывает на правильный IP адрес

### Полезные команды:

```bash
# Проверка стека
cdk list
cdk diff

# Уничтожение стека
cdk destroy

# Просмотр outputs
aws cloudformation describe-stacks --stack-name BirthdayBotStack
```
