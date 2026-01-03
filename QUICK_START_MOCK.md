# 🚀 Быстрый старт: Тестирование через Postman с Mock

## Три шага

### 1️⃣ Запустите API
```powershell
cd backend/src/BirthdayBot.Api
dotnet run
```

### 2️⃣ Отправьте webhook в Postman

**POST** `http://localhost:8080/telegram/webhook`

**Headers:**
```
X-Telegram-Bot-Api-Secret-Token: 35309489b499f510d3c7e7034fef56ac04cb5d9d0288e053
Content-Type: application/json
```

**Body:**
```json
{
  "update_id": 1,
  "message": {
    "message_id": 1,
    "from": {
      "id": 123456789,
      "is_bot": false,
      "first_name": "Test"
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

### 3️⃣ Проверьте ответы бота

**GET** `http://localhost:8080/api/mock/messages/123456789`

Увидите все сообщения, которые бот "отправил".

---

## ✅ Готово!

Теперь можете тестировать все команды через Postman без туннеля и без реального Telegram.

Подробная инструкция: `LOCAL_TESTING_WITH_MOCK.md`

