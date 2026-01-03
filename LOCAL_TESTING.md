# Локальное тестирование BirthdayBot

Этот документ описывает различные способы локального тестирования приложения.

## 🚀 Быстрый старт

### Использование PowerShell скриптов (Windows)

Для упрощения работы доступны готовые скрипты:

```powershell
# Запуск всех сервисов через Docker Compose
.\scripts\local-test.ps1 start

# Просмотр логов
.\scripts\local-test.ps1 logs

# Проверка health
.\scripts\local-test.ps1 health

# Установка webhook в Telegram
.\scripts\local-test.ps1 webhook

# Запуск юнит-тестов
.\scripts\local-test.ps1 test

# Разработка с hot reload (без Docker)
.\scripts\local-dev.ps1
```

Доступные команды: `start`, `stop`, `restart`, `logs`, `health`, `webhook`, `test`, `mongo-shell`, `clean`

## Содержание

1. [Быстрый старт через Docker Compose](#1-быстрый-старт-через-docker-compose)
2. [Разработка с hot reload (dotnet run)](#2-разработка-с-hot-reload-dotnet-run)
3. [Юнит-тесты](#3-юнит-тесты)
4. [Интеграционные тесты](#4-интеграционные-тесты)
5. [Локальный туннель без ngrok](#5-локальный-туннель-без-ngrok)
6. [Тестирование без реального Telegram бота](#6-тестирование-без-реального-telegram-бота)
7. [Проверка health и метрик](#7-проверка-health-и-метрик)

---

## 1. Быстрый старт через Docker Compose

### Требования
- Docker Desktop (Windows/Mac) или Docker + Docker Compose (Linux)
- Telegram Bot Token от @BotFather
- (Опционально) ngrok аккаунт для публичного URL

### Шаги

1. **Создайте `.env` файл в корне проекта:**
```bash
TELEGRAM_BOT_TOKEN=your_bot_token_here
TELEGRAM_WEBHOOK_SECRET=your_random_secret_here
NGROK_AUTHTOKEN=your_ngrok_token_here  # опционально
NGROK_DOMAIN=your-domain.ngrok-free.app  # опционально
```

2. **Запустите все сервисы:**
```bash
docker-compose up -d --build
```

3. **Проверьте статус:**
```bash
docker-compose ps
docker-compose logs -f api
```

4. **Если используете ngrok, получите публичный URL:**
```bash
docker-compose logs ngrok | grep "started tunnel"
```

5. **Установите webhook в Telegram:**
```bash
# PowerShell
$token = $env:TELEGRAM_BOT_TOKEN
$secret = $env:TELEGRAM_WEBHOOK_SECRET
$url = "https://your-domain.ngrok-free.app/telegram/webhook"

curl.exe -X POST "https://api.telegram.org/bot$token/setWebhook" `
  -H "Content-Type: application/json" `
  -d "{\"url\":\"$url\",\"secret_token\":\"$secret\"}"
```

6. **Проверьте health:**
```bash
curl http://localhost:8080/health/ready
curl http://localhost:8080/metrics
```

### Остановка
```bash
docker-compose down
# Для полной очистки (включая volumes):
docker-compose down -v
```

---

## 2. Разработка с hot reload (dotnet run)

Этот способ полезен для активной разработки с автоматической перезагрузкой при изменениях.

### Требования
- .NET 8 SDK
- MongoDB (локально или через Docker)

### Шаги

1. **Запустите MongoDB локально (если не используете Docker):**
```bash
docker run -d --name mongo-local -p 27017:27017 mongo:6.0 --replSet rs0
docker exec -it mongo-local mongosh --eval "rs.initiate({_id:'rs0', members:[{_id:0, host:'localhost:27017'}]})"
```

2. **Настройте переменные окружения:**
```powershell
# PowerShell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:MONGODB_URI = "mongodb://localhost:27017/birthdays?replicaSet=rs0"
$env:TELEGRAM_BOT_TOKEN = "your_bot_token_here"
$env:TELEGRAM_WEBHOOK_SECRET = "your_random_secret_here"
```

3. **Запустите приложение с hot reload:**
```bash
cd backend/src/BirthdayBot.Api
dotnet watch run
```

4. **Или используйте launchSettings.json:**
```bash
dotnet run --launch-profile BirthdayBot.Api
```

5. **Для публичного доступа используйте туннель (см. раздел 5)**

### Преимущества
- Быстрая перезагрузка при изменениях кода
- Полный доступ к отладчику
- Прямой доступ к логам в консоли

---

## 3. Юнит-тесты

Проект уже содержит юнит-тесты на xUnit.

### Запуск тестов

```bash
cd backend/tests/BirthdayBot.Tests
dotnet test
```

### Запуск с покрытием кода

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Запуск конкретного теста

```bash
dotnet test --filter "FullyQualifiedName~DateHelpersTests"
```

### Добавление новых тестов

Создайте новый класс в `backend/tests/BirthdayBot.Tests/`:

```csharp
using Xunit;
using FluentAssertions;

namespace BirthdayBot.Tests;

public class MyServiceTests
{
    [Fact]
    public void MyService_Should_DoSomething()
    {
        // Arrange
        var service = new MyService();
        
        // Act
        var result = service.DoSomething();
        
        // Assert
        result.Should().NotBeNull();
    }
}
```

---

## 4. Интеграционные тесты

Для тестирования с реальной MongoDB можно использовать Testcontainers.

### Установка Testcontainers

Добавьте в `BirthdayBot.Tests.csproj`:

```xml
<PackageReference Include="Testcontainers.MongoDb" Version="3.9.0" />
```

### Пример интеграционного теста

```csharp
using Testcontainers.MongoDb;
using Xunit;

namespace BirthdayBot.Tests.Integration;

public class MongoIntegrationTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder()
        .WithImage("mongo:6.0")
        .Build();

    public async Task InitializeAsync()
    {
        await _mongoContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _mongoContainer.DisposeAsync();
    }

    [Fact]
    public async Task Should_Connect_To_Mongo()
    {
        var connectionString = _mongoContainer.GetConnectionString();
        // Тестируйте вашу логику работы с MongoDB
    }
}
```

---

## 5. Локальный туннель без ngrok

Если не хотите использовать ngrok, есть альтернативы:

### Вариант A: Cloudflare Tunnel (cloudflared)

1. **Установите cloudflared:**
```powershell
# Windows (Chocolatey)
choco install cloudflared

# Или скачайте с https://github.com/cloudflare/cloudflared/releases
```

2. **Запустите туннель:**
```bash
cloudflared tunnel --url http://localhost:8080
```

3. **Используйте полученный URL для webhook**

### Вариант B: LocalTunnel

```bash
npx localtunnel --port 8080
```

### Вариант C: Serveo (SSH туннель)

```bash
ssh -R 80:localhost:8080 serveo.net
```

### Вариант D: Telebit (требует регистрации)

```bash
npm install -g telebit
telebit http 8080
```

---

## 6. Тестирование без реального Telegram бота

Для тестирования логики без реального Telegram API можно использовать моки.

### Создание мок-сервиса для Telegram Bot

1. **Создайте интерфейс:**
```csharp
// backend/src/BirthdayBot.Application/Interfaces/ITelegramBotClient.cs
public interface ITelegramBotClient
{
    Task SendTextMessageAsync(long chatId, string text, CancellationToken ct = default);
    Task SetWebhookAsync(string url, string? secretToken = null, CancellationToken ct = default);
}
```

2. **Создайте мок-реализацию:**
```csharp
// backend/tests/BirthdayBot.Tests/Mocks/MockTelegramBotClient.cs
public class MockTelegramBotClient : ITelegramBotClient
{
    public List<(long ChatId, string Text)> SentMessages { get; } = new();
    
    public Task SendTextMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        SentMessages.Add((chatId, text));
        return Task.CompletedTask;
    }
    
    public Task SetWebhookAsync(string url, string? secretToken = null, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
```

3. **Используйте в тестах:**
```csharp
[Fact]
public async Task Should_Send_Message_On_Command()
{
    // Arrange
    var mockBot = new MockTelegramBotClient();
    var handler = new UpdateHandler(mockBot, /* другие зависимости */);
    var update = CreateTestUpdate("/start");
    
    // Act
    await handler.HandleUpdateAsync(update, CancellationToken.None);
    
    // Assert
    mockBot.SentMessages.Should().HaveCount(1);
    mockBot.SentMessages[0].Text.Should().Contain("Welcome");
}
```

### Тестирование webhook endpoint

Используйте `TestServer` из `Microsoft.AspNetCore.TestHost`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

public class WebhookTests
{
    [Fact]
    public async Task Webhook_Should_Return_Ok_For_Valid_Update()
    {
        // Arrange
        var hostBuilder = new WebHostBuilder()
            .UseStartup<Program>()
            .ConfigureServices(services =>
            {
                // Замените реальные сервисы на моки
            });

        using var server = new TestServer(hostBuilder);
        var client = server.CreateClient();
        
        var update = new { message = new { text = "/start" } };
        var content = new StringContent(
            JsonConvert.SerializeObject(update),
            Encoding.UTF8,
            "application/json"
        );
        
        // Act
        var response = await client.PostAsync("/telegram/webhook", content);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

---

## 7. Проверка health и метрик

### Health Checks

```bash
# Liveness probe
curl http://localhost:8080/health/live

# Readiness probe
curl http://localhost:8080/health/ready

# Startup probe
curl http://localhost:8080/health/startup

# Simple health check
curl http://localhost:8080/healthz
```

### Prometheus метрики

```bash
curl http://localhost:8080/metrics
```

### Проверка через браузер

Откройте:
- Health: http://localhost:8080/health/ready
- Metrics: http://localhost:8080/metrics
- Root: http://localhost:8080/

---

## Полезные команды для отладки

### Просмотр логов MongoDB

```bash
docker-compose logs -f mongodb
```

### Подключение к MongoDB через mongosh

```bash
docker-compose exec mongodb mongosh
```

### Проверка переменных окружения в контейнере

```bash
docker-compose exec api env | grep -E "TELEGRAM|MONGO"
```

### Перезапуск только API сервиса

```bash
docker-compose restart api
```

### Просмотр использования ресурсов

```bash
docker stats
```

---

## Чеклист для локального тестирования

- [ ] MongoDB запущен и доступен
- [ ] Переменные окружения настроены корректно
- [ ] Приложение запускается без ошибок
- [ ] Health checks возвращают OK
- [ ] Webhook установлен в Telegram
- [ ] Бот отвечает на команду /start
- [ ] Можно добавить день рождения через /add_birthday
- [ ] Список дней рождения отображается через /list
- [ ] Метрики доступны на /metrics
- [ ] Логи не содержат ошибок

---

## Решение проблем

### Проблема: Webhook возвращает 401

**Решение:** Проверьте, что `TELEGRAM_WEBHOOK_SECRET` совпадает с `secret_token` в `setWebhook`.

### Проблема: MongoDB connection failed

**Решение:** 
- Убедитесь, что MongoDB запущен: `docker-compose ps`
- Проверьте connection string: `mongodb://mongodb:27017/birthdays?replicaSet=rs0`
- Проверьте логи: `docker-compose logs mongodb`

### Проблема: Приложение не запускается

**Решение:**
- Проверьте логи: `docker-compose logs api`
- Убедитесь, что все переменные окружения установлены
- Проверьте, что порт 8080 не занят: `netstat -ano | findstr :8080`

### Проблема: Hot reload не работает

**Решение:**
- Убедитесь, что используете `dotnet watch run`
- Проверьте, что файлы не игнорируются в `.gitignore`
- Перезапустите с флагом `--no-hot-reload` для диагностики

---

## Дополнительные ресурсы

- [Telegram Bot API Documentation](https://core.telegram.org/bots/api)
- [MongoDB .NET Driver](https://www.mongodb.com/docs/drivers/csharp/)
- [ASP.NET Core Testing](https://learn.microsoft.com/en-us/aspnet/core/test/)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)

