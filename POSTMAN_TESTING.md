# Тестирование API через Postman

## 📋 Доступные Endpoints

### 1. Корневой endpoint (GET)
**URL:** `http://localhost:8080/`

**Метод:** `GET`

**Описание:** Простая проверка, что API работает

**Ожидаемый ответ:**
```json
{
  "name": "BirthdayBot",
  "status": "ok"
}
```

---

### 2. Health Check - Simple (GET)
**URL:** `http://localhost:8080/healthz`

**Метод:** `GET`

**Описание:** Простая проверка здоровья

**Ожидаемый ответ:**
```
ok
```

**Status Code:** `200 OK`

---

### 3. Health Check - Liveness (GET)
**URL:** `http://localhost:8080/health/live`

**Метод:** `GET`

**Описание:** Liveness probe для Kubernetes/Docker

**Ожидаемый ответ:**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0123456"
}
```

**Status Code:** `200 OK`

---

### 4. Health Check - Readiness (GET)
**URL:** `http://localhost:8080/health/ready`

**Метод:** `GET`

**Описание:** Readiness probe (проверяет подключение к MongoDB)

**Ожидаемый ответ:**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0123456",
  "entries": {
    "mongodb": {
      "status": "Healthy",
      "duration": "00:00:00.0012345"
    }
  }
}
```

**Status Code:** `200 OK`

**Если MongoDB недоступен:**
```json
{
  "status": "Unhealthy",
  "entries": {
    "mongodb": {
      "status": "Unhealthy",
      "description": "MongoDB connection failed"
    }
  }
}
```

**Status Code:** `503 Service Unavailable`

---

### 5. Health Check - Startup (GET)
**URL:** `http://localhost:8080/health/startup`

**Метод:** `GET`

**Описание:** Startup probe

**Ожидаемый ответ:** Аналогично `/health/live`

---

### 6. Prometheus Metrics (GET)
**URL:** `http://localhost:8080/metrics`

**Метод:** `GET`

**Описание:** Метрики в формате Prometheus

**Ожидаемый ответ:**
```
# HELP http_server_request_duration_seconds The duration of HTTP server requests.
# TYPE http_server_request_duration_seconds histogram
http_server_request_duration_seconds_bucket{le="0.005",method="GET",route="/healthz"} 1
...
```

**Status Code:** `200 OK`

---

### 7. Telegram Webhook (POST)
**URL:** `http://localhost:8080/telegram/webhook`

**Метод:** `POST`

**Описание:** Webhook endpoint для получения обновлений от Telegram

**Headers:**
```
Content-Type: application/json
X-Telegram-Bot-Api-Secret-Token: <ваш_webhook_secret> (опционально, если настроен)
```

**Body (JSON):**
```json
{
  "update_id": 123456789,
  "message": {
    "message_id": 1,
    "from": {
      "id": 123456789,
      "is_bot": false,
      "first_name": "Test",
      "username": "testuser"
    },
    "chat": {
      "id": 123456789,
      "type": "private"
    },
    "date": 1704288000,
    "text": "/start"
  }
}
```

**Ожидаемый ответ:**
```
(пустое тело)
```

**Status Code:** `200 OK`

**Возможные ошибки:**
- `401 Unauthorized` - неверный `X-Telegram-Bot-Api-Secret-Token`
- `400 Bad Request` - невалидный JSON или пустой update

---

## 🚀 Настройка Postman

### Шаг 1: Создайте новую коллекцию

1. Откройте Postman
2. Нажмите **"New"** → **"Collection"**
3. Назовите коллекцию: `BirthdayBot API`

### Шаг 2: Создайте переменные окружения

1. Нажмите на иконку **"Environments"** (слева)
2. Создайте новое окружение: `BirthdayBot Local`
3. Добавьте переменные:
   - `base_url`: `http://localhost:8080`
   - `webhook_secret`: `<ваш_webhook_secret>` (если используется)

4. Сохраните и выберите это окружение

### Шаг 3: Создайте запросы

#### Запрос 1: Root Endpoint
- **Method:** `GET`
- **URL:** `{{base_url}}/`
- **Name:** `Root - Health Check`

#### Запрос 2: Simple Health
- **Method:** `GET`
- **URL:** `{{base_url}}/healthz`
- **Name:** `Health - Simple`

#### Запрос 3: Readiness Check
- **Method:** `GET`
- **URL:** `{{base_url}}/health/ready`
- **Name:** `Health - Readiness (with MongoDB)`

#### Запрос 4: Metrics
- **Method:** `GET`
- **URL:** `{{base_url}}/metrics`
- **Name:** `Prometheus Metrics`

#### Запрос 5: Telegram Webhook
- **Method:** `POST`
- **URL:** `{{base_url}}/telegram/webhook`
- **Headers:**
  - `Content-Type`: `application/json`
  - `X-Telegram-Bot-Api-Secret-Token`: `{{webhook_secret}}` (если используется)
- **Body:** 
  - Выберите `raw` → `JSON`
  - Вставьте пример JSON из раздела выше
- **Name:** `Telegram Webhook`

---

## 📝 Примеры тестовых сценариев

### Сценарий 1: Проверка базовой работоспособности

1. **GET** `http://localhost:8080/`
   - Ожидается: `200 OK` с `{"name": "BirthdayBot", "status": "ok"}`

2. **GET** `http://localhost:8080/healthz`
   - Ожидается: `200 OK` с `"ok"`

### Сценарий 2: Проверка подключения к MongoDB

1. **GET** `http://localhost:8080/health/ready`
   - Если MongoDB доступен: `200 OK` с `"status": "Healthy"`
   - Если MongoDB недоступен: `503 Service Unavailable` с `"status": "Unhealthy"`

### Сценарий 3: Тестирование Telegram Webhook

1. **POST** `http://localhost:8080/telegram/webhook`
   - Body: JSON с командой `/start`
   - Ожидается: `200 OK`

2. **POST** `http://localhost:8080/telegram/webhook`
   - Body: Невалидный JSON
   - Ожидается: `400 Bad Request`

3. **POST** `http://localhost:8080/telegram/webhook`
   - Headers: Неверный `X-Telegram-Bot-Api-Secret-Token`
   - Ожидается: `401 Unauthorized`

---

## 🔍 Проверка Rate Limiting

Webhook endpoint имеет rate limiting: **60 запросов в минуту на IP**.

Для проверки:
1. Отправьте 60+ запросов подряд
2. После 60-го запроса должен вернуться `429 Too Many Requests`

---

## 📊 Примеры ответов

### Успешный Health Check
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0012345",
  "entries": {
    "mongodb": {
      "status": "Healthy",
      "duration": "00:00:00.0001234",
      "tags": []
    }
  }
}
```

### Неуспешный Health Check (MongoDB недоступен)
```json
{
  "status": "Unhealthy",
  "totalDuration": "00:00:00.0300000",
  "entries": {
    "mongodb": {
      "status": "Unhealthy",
      "description": "MongoDB.Driver.MongoConnectionException: Unable to connect to server...",
      "duration": "00:00:00.0300000",
      "tags": []
    }
  }
}
```

### Rate Limit Error
```
Status: 429 Too Many Requests
Body: (пустое)
```

---

## 🛠️ Troubleshooting

### Проблема: Connection refused

**Решение:**
- Убедитесь, что API запущен на `http://localhost:8080`
- Проверьте логи приложения

### Проблема: Health check показывает Unhealthy

**Решение:**
- Проверьте, что SSM туннель к MongoDB активен
- Проверьте connection string в `appsettings.Development.json`
- Проверьте логи приложения на ошибки подключения

### Проблема: Webhook возвращает 401

**Решение:**
- Проверьте, что `X-Telegram-Bot-Api-Secret-Token` совпадает с настройками
- Или убедитесь, что webhook secret не настроен (тогда header не нужен)

---

## 📦 Импорт коллекции Postman

Вы можете создать JSON файл для импорта в Postman:

```json
{
  "info": {
    "name": "BirthdayBot API",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "item": [
    {
      "name": "Root",
      "request": {
        "method": "GET",
        "header": [],
        "url": {
          "raw": "{{base_url}}/",
          "host": ["{{base_url}}"],
          "path": [""]
        }
      }
    },
    {
      "name": "Health - Readiness",
      "request": {
        "method": "GET",
        "header": [],
        "url": {
          "raw": "{{base_url}}/health/ready",
          "host": ["{{base_url}}"],
          "path": ["health", "ready"]
        }
      }
    }
  ],
  "variable": [
    {
      "key": "base_url",
      "value": "http://localhost:8080"
    }
  ]
}
```

Сохраните как `BirthdayBot.postman_collection.json` и импортируйте в Postman.

