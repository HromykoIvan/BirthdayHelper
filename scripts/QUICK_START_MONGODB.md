# 🚀 Быстрый старт: MongoDB Compass

## Три команды для подключения

### 1️⃣ Откройте туннель (оставьте окно открытым!)
```powershell
npm run mongo:tunnel
```

### 2️⃣ В новом окне PowerShell - получите URI
```powershell
npm run mongo:uri
```

### 3️⃣ Откройте Compass
```powershell
npm run mongo:compass
```

---

## Если что-то не работает

### Проверка туннеля
```powershell
Test-NetConnection -ComputerName localhost -Port 27017
```

### Прямое подключение с Instance ID
```powershell
aws ssm start-session `
  --region eu-central-1 `
  --target i-0f84acabd8293b119 `
  --document-name AWS-StartPortForwardingSession `
  --parameters "localPortNumber=27017,portNumber=27017"
```

### Connection string для Compass
```
mongodb://localhost:27017/birthdays?directConnection=true
```

---

## Важно

- ✅ Туннель должен быть открыт (не закрывайте окно!)
- ✅ Используйте `localhost:27017`, не Public IP
- ✅ В Compass отключите TLS/SSL
- ✅ Database name: `birthdays`

