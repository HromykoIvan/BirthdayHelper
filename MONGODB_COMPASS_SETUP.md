# Подключение к MongoDB через MongoDB Compass

Пошаговая инструкция для подключения к MongoDB инстансу на EC2 через SSM туннель.

## 📋 Предварительные требования

### 1. Установите необходимые инструменты

**AWS CLI v2**
```powershell
# Проверка версии
aws --version
```
Если не установлен: https://aws.amazon.com/cli/

**Session Manager Plugin**
```powershell
# Проверка установки
session-manager-plugin --version
```
Если не установлен: https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html

**MongoDB Compass**
```powershell
# Проверка установки (если установлен через Chocolatey)
compass --version
```
Если не установлен: https://www.mongodb.com/try/download/compass

**PowerShell 7** (рекомендуется)
```powershell
pwsh --version
```
Если не установлен: https://github.com/PowerShell/PowerShell/releases

### 2. Настройте AWS credentials

```powershell
# Проверка текущих credentials
aws sts get-caller-identity

# Если не настроено, выполните:
aws configure
# Или для SSO:
aws configure sso
```

### 3. Проверьте IAM права

Убедитесь, что у вашего AWS пользователя/роли есть права:
- `ssm:StartSession`
- `ssm:DescribeInstances`
- `secretsmanager:GetSecretValue`

---

## 🚀 Пошаговая инструкция

### Шаг 1: Откройте SSM туннель к MongoDB

**Важно:** Этот процесс должен оставаться запущенным. Не закрывайте окно!

#### Вариант A: Используя npm скрипт (рекомендуется)

```powershell
# В корне проекта
npm run mongo:tunnel
```

#### Вариант B: Прямой запуск скрипта

```powershell
.\scripts\mongo-tunnel-ssm.ps1
```

#### Вариант C: Ручной запуск с указанием Instance ID

Если автоматический поиск не работает, укажите Instance ID напрямую:

```powershell
aws ssm start-session `
  --region eu-central-1 `
  --target i-0f84acabd8293b119 `
  --document-name AWS-StartPortForwardingSession `
  --parameters "localPortNumber=27017,portNumber=27017"
```

**Ожидаемый вывод:**
```
>> Searching for EC2 by tag Name=BirthdayBotStack/MongoInstance in eu-central-1 ...
>> EC2: i-0f84acabd8293b119
>> Port forwarding: localhost:27017 -> i-0f84acabd8293b119:27017
>> Keep this window open. Stop: Ctrl+C

Starting session with SessionId: ...
Port 27017 opened for sessionId ...
Waiting for connections...
```

**⚠️ Оставьте это окно открытым!** Туннель работает пока окно активно.

---

### Шаг 2: Получите connection string

Откройте **новое** окно PowerShell (не закрывая туннель):

#### Вариант A: Используя npm скрипт

```powershell
npm run mongo:uri
```

#### Вариант B: Прямой запуск

```powershell
.\scripts\mongo-uri.ps1
```

**Ожидаемый вывод:**
```
mongodb://localhost:27017/birthdays?directConnection=true
```

**Скопируйте этот URI** - он понадобится для Compass.

---

### Шаг 3: Откройте MongoDB Compass

#### Вариант A: Автоматический запуск (если зарегистрирован handler)

```powershell
npm run mongo:compass
```

Это автоматически:
1. Получит connection string
2. Попытается открыть Compass с этим URI

#### Вариант B: Ручной запуск

1. Откройте **MongoDB Compass** вручную
2. В поле "New Connection" вставьте URI из Шага 2:
   ```
   mongodb://localhost:27017/birthdays?directConnection=true
   ```
3. Нажмите **Connect**

---

### Шаг 4: Настройки подключения в Compass

Если автоматическое подключение не сработало:

1. **Откройте MongoDB Compass**
2. Нажмите **"New Connection"** или **"Fill in connection fields individually"**
3. Заполните поля:
   - **Hostname:** `localhost`
   - **Port:** `27017`
   - **Authentication:** `None` (если MongoDB без аутентификации)
   - **Default auth DB:** оставьте пустым
4. Перейдите на вкладку **"More Options"**
5. В поле **"Default Database"** введите: `birthdays`
6. Нажмите **"Connect"**

---

## ✅ Проверка подключения

После успешного подключения вы должны увидеть:

1. **Список баз данных:**
   - `birthdays` (основная БД)
   - `admin`
   - `config`
   - `local`

2. **Коллекции в базе `birthdays`:**
   - `users` - пользователи бота
   - `birthdays` - дни рождения
   - `delivery_logs` - логи доставки

3. **Данные:**
   - Откройте коллекцию `users` - увидите пользователей Telegram
   - Откройте коллекцию `birthdays` - увидите дни рождения
   - Откройте коллекцию `delivery_logs` - увидите историю отправок

---

## 🔧 Решение проблем

### Проблема: "Instance not found"

**Решение:**
1. Проверьте, что инстанс запущен:
   ```powershell
   aws ec2 describe-instances --instance-ids i-0f84acabd8293b119 --region eu-central-1
   ```

2. Проверьте правильность тега:
   ```powershell
   aws ec2 describe-instances --filters "Name=tag:Name,Values=BirthdayBotStack/MongoInstance" --region eu-central-1
   ```

3. Укажите Instance ID напрямую (см. Шаг 1, Вариант C)

### Проблема: "session-manager-plugin not found"

**Решение:**
1. Установите Session Manager Plugin:
   - Windows: https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html#install-plugin-windows

2. Проверьте PATH:
   ```powershell
   $env:PATH -split ';' | Select-String "SessionManagerPlugin"
   ```

### Проблема: "Port 27017 already in use"

**Решение:**
1. Найдите процесс, использующий порт:
   ```powershell
   netstat -ano | findstr :27017
   ```

2. Остановите процесс или используйте другой порт:
   ```powershell
   .\scripts\mongo-tunnel-ssm.ps1 -LocalPort 27018
   # Затем в Compass используйте порт 27018
   ```

### Проблема: "Failed to read secret"

**Решение:**
1. Проверьте AWS credentials:
   ```powershell
   aws sts get-caller-identity
   ```

2. Проверьте права на Secrets Manager:
   ```powershell
   aws secretsmanager get-secret-value --secret-id birthday-bot/mongo-url --region eu-central-1
   ```

3. Проверьте имя секрета (может отличаться)

### Проблема: Compass не подключается

**Решение:**
1. Убедитесь, что туннель активен (окно PowerShell открыто)
2. Проверьте подключение к localhost:
   ```powershell
   Test-NetConnection -ComputerName localhost -Port 27017
   ```

3. Попробуйте подключиться через mongosh:
   ```powershell
   mongosh mongodb://localhost:27017/birthdays
   ```

4. В Compass отключите TLS/SSL (если включено)

### Проблема: "Connection timeout"

**Решение:**
1. Проверьте, что туннель работает (окно не закрыто)
2. Проверьте Security Group - MongoDB должен быть доступен из Bot SG
3. Проверьте, что MongoDB контейнер запущен на EC2:
   ```powershell
   aws ssm start-session --target i-0f84acabd8293b119 --region eu-central-1
   # В сессии:
   sudo docker ps
   sudo docker logs mongo
   ```

---

## 📝 Альтернативные способы подключения

### Способ 1: Прямое подключение через SSM (без туннеля)

Если нужно только выполнить команды в MongoDB:

```powershell
# Подключение к инстансу
aws ssm start-session --target i-0f84acabd8293b119 --region eu-central-1

# В сессии выполните:
sudo docker exec -it mongo mongosh
```

### Способ 2: Использование mongosh локально

Если установлен mongosh локально:

```powershell
# После запуска туннеля (Шаг 1)
mongosh mongodb://localhost:27017/birthdays
```

### Способ 3: Изменение порта туннеля

Если порт 27017 занят:

```powershell
# Запустите туннель на другом порту
.\scripts\mongo-tunnel-ssm.ps1 -LocalPort 27018

# В Compass используйте:
mongodb://localhost:27018/birthdays?directConnection=true
```

---

## 🔒 Безопасность

✅ **Безопасно:**
- Подключение через SSM зашифровано
- Не нужно открывать порт 27017 в Security Group
- Используется IAM аутентификация
- Все сессии логируются в CloudTrail

❌ **Не делайте:**
- Не открывайте порт 27017 публично в Security Group
- Не храните connection strings в коде
- Не коммитьте credentials в git

---

## 📊 Полезные команды для работы с данными

После подключения в Compass вы можете:

1. **Просматривать данные:**
   - Откройте коллекцию → Documents
   - Используйте фильтры для поиска

2. **Выполнять запросы:**
   - Вкладка "Documents" → "Filter" → введите JSON:
   ```json
   { "TelegramUserId": 123456789 }
   ```

3. **Создавать индексы:**
   - Вкладка "Indexes" → "Create Index"

4. **Экспортировать данные:**
   - Documents → "Export Collection"

---

## 🎯 Быстрая справка

```powershell
# 1. Запустить туннель (оставить открытым)
npm run mongo:tunnel

# 2. В новом окне - получить URI
npm run mongo:uri

# 3. Открыть Compass
npm run mongo:compass
```

**Или все вручную:**
```powershell
# Терминал 1: Туннель
.\scripts\mongo-tunnel-ssm.ps1

# Терминал 2: URI и Compass
.\scripts\mongo-uri.ps1
.\scripts\mongo-compass.ps1
```

---

## 📞 Дополнительная помощь

Если проблемы остаются:

1. Проверьте логи туннеля в окне PowerShell
2. Проверьте логи MongoDB на EC2:
   ```powershell
   aws ssm start-session --target i-0f84acabd8293b119 --region eu-central-1
   sudo docker logs mongo
   ```
3. Проверьте CloudWatch Logs для SSM сессий
4. Убедитесь, что все инструменты установлены и в PATH

