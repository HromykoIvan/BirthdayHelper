// path: backend/src/BirthdayBot.Infrastructure/Services/ReminderHostedService.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Utils;
using Cronos;
using MongoDB.Bson;
using NodaTime;
using Telegram.Bot;

namespace BirthdayBot.Infrastructure.Services;

public class ReminderHostedService : BackgroundService, IReminderService
{
    private readonly ILogger<ReminderHostedService> _logger;
    private readonly IUserRepository _users;
    private readonly IBirthdayRepository _birthdays;
    private readonly IDeliveryLogRepository _logs;
    private readonly IGreetingGenerator _greetings;
    private readonly ILocalizationService _i18n;
    private readonly ITelegramBotClient _bot;
    private readonly string _cron;
    private readonly DateTimeZoneProvider _tzdb = DateTimeZoneProviders.Tzdb;

    public ReminderHostedService(
        ILogger<ReminderHostedService> logger,
        IUserRepository users,
        IBirthdayRepository birthdays,
        IDeliveryLogRepository logs,
        IGreetingGenerator greetings,
        ILocalizationService i18n,
        ITelegramBotClient bot,
        Microsoft.Extensions.Options.IOptions<BirthdayBot.Api.Options.ReminderOptions> options)
    {
        _logger = logger;
        _users = users;
        _birthdays = birthdays;
        _logs = logs;
        _greetings = greetings;
        _i18n = i18n;
        _bot = bot;
        _cron = options.Value.Cron ?? "* * * * *"; // every minute
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var expr = CronExpression.Parse(_cron);
        while (!stoppingToken.IsCancellationRequested)
        {
            var utcNow = DateTimeOffset.UtcNow;
            var next = expr.GetNextOccurrence(utcNow.UtcDateTime, TimeZoneInfo.Utc);
            var delay = next.HasValue ? next.Value - utcNow.UtcDateTime : TimeSpan.FromMinutes(1);
            if (delay < TimeSpan.Zero) delay = TimeSpan.FromSeconds(10);
            await Task.Delay(delay, stoppingToken).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reminder run failed");
                }
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        // For all users: if now is their NotifyAtLocalTime minute, send reminders for today and tomorrow.
        // To avoid full scan in large scale, you'd store schedule index; here it's MVP.
        var users = await (await _usersQueryable()).ToListAsync(ct);

        foreach (var user in users)
        {
            try
            {
                if (!_tzdb.Ids.Contains(user.Timezone)) continue;
                var zone = _tzdb[user.Timezone];
                var nowZoned = SystemClock.Instance.GetCurrentInstant().InZone(zone);
                var todayLocal = nowZoned.Date;

                if (!DateHelpers.TryParseTimeHHmm(user.NotifyAtLocalTime, out var hh, out var mm))
                    continue;

                if (nowZoned.TimeOfDay.Hour != hh || nowZoned.TimeOfDay.Minute != mm) continue;

                var list = await _birthdays.ListByUserAsync(user.Id, ct);
                foreach (var b in list)
                {
                    var (next, age) = DateHelpers.NextBirthday(todayLocal, b.DateOfBirth);
                    var isToday = next == todayLocal;
                    var isTomorrow = next == todayLocal.PlusDays(1);

                    if (!isToday && !isTomorrow) continue;

                    var when = isToday ? (user.Lang == Language.Pl ? "DZIŚ" : user.Lang == Language.Ru ? "СЕГОДНЯ" : "TODAY")
                                       : (user.Lang == Language.Pl ? "JUTRO" : user.Lang == Language.Ru ? "ЗАВТРА" : "TOMORROW");

                    var msg = $"{when}: {b.Name} — {next:yyyy-MM-dd} ({age})";
                    if (user.AutoGenerateGreetings)
                    {
                        var text = _greetings.Generate(user.Lang, user.Tone, b.Name, age);
                        msg += $"\n\n{text}";
                    }

                    var sent = await _bot.SendTextMessageAsync(chatId: user.TelegramUserId, text: msg, cancellationToken: ct);
                    await _logs.CreateAsync(new DeliveryLog
                    {
                        UserId = user.Id,
                        BirthdayId = b.Id,
                        WhenUtc = DateTime.UtcNow,
                        MessageId = sent.MessageId.ToString(),
                        Status = "Sent"
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder error for user {User}", user.TelegramUserId);
            }
        }

        async Task<IAsyncCursor<BirthdayBot.Domain.Entities.User>> _usersQueryable()
        {
            // Small helper to get all users (MVP). For large scale you would paginate.
            var collection = new MongoDB.Driver.MongoClient(
                new MongoDB.Driver.MongoUrl(Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://mongodb:27017/birthdays"))
                .GetDatabase(Environment.GetEnvironmentVariable("MONGO_DBNAME") ?? "birthdays")
                .GetCollection<BirthdayBot.Domain.Entities.User>("users");
            return await collection.FindAsync(_ => true, cancellationToken: ct);
        }
    }
}