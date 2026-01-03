# Настройка локального webhook для тестирования

## 🎯 Цель

Настроить локальный туннель, чтобы Telegram отправлял webhook на ваш локальный сервер вместо AWS.

---

## 📋 Предварительные требования

1. **API должен быть запущен** на `http://localhost:8080`
2. **`.env` файл** с `TELEGRAM_BOT_TOKEN` и `TELEGRAM_WEBHOOK_SECRET`
3. **Туннель** (cloudflared или ngrok)

---

## 🚀 Быстрый старт

### Вариант 1: Cloudflared (рекомендуется, бесплатно, без регистрации)

#### Шаг 1: Установите cloudflared

```powershell
# Через Chocolatey
choco install cloudflared

# Или скачайте с https://github.com/cloudflare/cloudflared/releases
```

#### Шаг 2: Запустите API

```powershell
# В одном терминале
cd backend/src/BirthdayBot.Api
dotnet run

# Или используйте скрипт
.\scripts\local-dev.ps1
```

#### Шаг 3: Запустите туннель и установите webhook

```powershell
.\scripts\local-webhook.ps1
```

Скрипт автоматически:
1. ✅ Проверит, что API запущен
2. ✅ Запустит cloudflared туннель
3. ✅ Получит публичный URL
4. ✅ Установит webhook в Telegram

---

### Вариант 2: Ngrok (требует регистрацию)

#### Шаг 1: Установите ngrok

```powershell
# Через Chocolatey
choco install ngrok

# Или скачайте с https://ngrok.com/download
```

#### Шаг 2: Получите auth token

1. Зарегистрируйтесь на https://ngrok.com
2. Получите auth token из dashboard
3. Добавьте в `.env`:
   ```
   NGROK_AUTHTOKEN=your_token_here
   ```

#### Шаг 3: Запустите туннель

```powershell
.\scripts\local-webhook.ps1 -TunnelType ngrok
```

---

## 📝 Ручная настройка

### Шаг 1: Запустите API

```powershell
cd backend/src/BirthdayBot.Api
dotnet run
```

API должен быть доступен на `http://localhost:8080`

### Шаг 2: Запустите туннель

#### Cloudflared:
```powershell
cloudflared tunnel --url http://localhost:8080
```

Вы увидите что-то вроде:
```
+--------------------------------------------------------------------------------------------+
|  Your quick Tunnel has been created! Visit it at (it may take some time to be reachable): |
|  https://random-name.trycloudflare.com                                                     |
+--------------------------------------------------------------------------------------------+
```

#### Ngrok:
```powershell
ngrok http 8080
```

Вы увидите:
```
Forwarding   https://xxxx-xx-xx-xx-xx.ngrok-free.app -> http://localhost:8080
```

### Шаг 3: Установите webhook

Скопируйте публичный URL из туннеля и выполните:

```powershell
$token = "YOUR_BOT_TOKEN"
$secret = "YOUR_WEBHOOK_SECRET"
$tunnelUrl = "https://your-tunnel-url.trycloudflare.com"  # или .ngrok-free.app

$body = @{
    url = "$tunnelUrl/telegram/webhook"
    secret_token = $secret
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/setWebhook" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

---

## ✅ Проверка

### 1. Проверьте webhook

```powershell
$token = "YOUR_BOT_TOKEN"
Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/getWebhookInfo"
```

Должно показать ваш локальный URL.

### 2. Напишите боту в Telegram

1. Откройте Telegram
2. Найдите вашего бота
3. Напишите `/start`
4. Проверьте логи API — должны появиться запросы

---

## 🔄 Переключение между локальным и AWS

### Установить webhook на локальный сервер:
```powershell
.\scripts\local-webhook.ps1
```

### Вернуть webhook на AWS:
```powershell
# Получите URL вашего AWS инстанса
$awsUrl = "https://your-domain.com"  # или IP

$token = $env:TELEGRAM_BOT_TOKEN
$secret = $env:TELEGRAM_WEBHOOK_SECRET

$body = @{
    url = "$awsUrl/telegram/webhook"
    secret_token = $secret
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/setWebhook" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body
```

### Удалить webhook (использовать polling):
```powershell
$token = "YOUR_BOT_TOKEN"
Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/deleteWebhook"
```

---

## 🛠️ Troubleshooting

### Проблема: "API is not running"

**Решение:**
1. Убедитесь, что API запущен на порту 8080
2. Проверьте: `curl http://localhost:8080/healthz`

### Проблема: "cloudflared not found"

**Решение:**
1. Установите cloudflared: `choco install cloudflared`
2. Или добавьте в PATH вручную

### Проблема: "Failed to get tunnel URL"

**Решение:**
1. Подождите несколько секунд (туннель может запускаться)
2. Проверьте логи: `Get-Content cloudflared.log`
3. Для ngrok: откройте http://localhost:4040 в браузере

### Проблема: Webhook не работает

**Решение:**
1. Проверьте, что туннель активен (окно не закрыто)
2. Проверьте webhook: `Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/getWebhookInfo"`
3. Проверьте логи API на наличие входящих запросов
4. Убедитесь, что секрет совпадает

### Проблема: "Not Found" при отправке сообщения

**Решение:**
Это нормально для тестовых запросов. Используйте реальный Telegram для полного тестирования.

---

## 📊 Сравнение туннелей

| Критерий | Cloudflared | Ngrok |
|----------|-------------|-------|
| **Регистрация** | ❌ Не требуется | ✅ Требуется |
| **Бесплатный tier** | ✅ Неограниченный | ⚠️ Ограниченный |
| **Стабильность URL** | ⚠️ Меняется при перезапуске | ✅ Можно зафиксировать |
| **Скорость** | ✅ Быстро | ✅ Быстро |
| **Установка** | Простая | Простая |

**Рекомендация:** Используйте **cloudflared** для локальной разработки (бесплатно, без регистрации).

---

## 🎯 Workflow для разработки

1. **Запустите API:**
   ```powershell
   .\scripts\local-dev.ps1
   ```

2. **В новом терминале запустите туннель:**
   ```powershell
   .\scripts\local-webhook.ps1
   ```

3. **Тестируйте в Telegram:**
   - Напишите боту команды
   - Проверьте ответы
   - Смотрите логи API

4. **После разработки:**
   - Остановите туннель (Ctrl+C)
   - Верните webhook на AWS (если нужно)

---

## 💡 Полезные команды

### Проверить текущий webhook:
```powershell
$token = $env:TELEGRAM_BOT_TOKEN
Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/getWebhookInfo" | ConvertTo-Json
```

### Удалить webhook:
```powershell
$token = $env:TELEGRAM_BOT_TOKEN
Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/deleteWebhook"
```

### Проверить статус API:
```powershell
Invoke-WebRequest -Uri "http://localhost:8080/health/ready" -UseBasicParsing
```

---

## 🔒 Безопасность

⚠️ **Важно:**
- Туннель делает ваш локальный API доступным из интернета
- Используйте только для разработки
- Не оставляйте туннель запущенным без необходимости
- Webhook secret защищает от несанкционированных запросов

✅ **Хорошие практики:**
- Используйте webhook secret
- Останавливайте туннель после разработки
- Не коммитьте токены в git

