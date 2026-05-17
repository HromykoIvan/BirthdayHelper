using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Utils;
using Cronos;
using MongoDB.Bson;
using MongoDB.Driver;
using NodaTime;
using Telegram.Bot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using BirthdayBot.Infrastructure.Options;
using Telegram.Bot.Types.ReplyMarkups;
using System.Diagnostics;

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
    private readonly IAiGreetingEnhancer _enhancer;
    private readonly IAiEventRepository _aiEvents;
    private readonly string _cron;
    private readonly IDateTimeZoneProvider _tzdb = DateTimeZoneProviders.Tzdb;

    public ReminderHostedService(
        ILogger<ReminderHostedService> logger,
        IUserRepository users,
        IBirthdayRepository birthdays,
        IDeliveryLogRepository logs,
        IGreetingGenerator greetings,
        IAiGreetingEnhancer enhancer,
        IAiEventRepository aiEvents,
        ILocalizationService i18n,
        ITelegramBotClient bot,
        IOptions<ReminderOptions> options)
    {
        _logger = logger;
        _users = users;
        _birthdays = birthdays;
        _logs = logs;
        _greetings = greetings;
        _enhancer = enhancer;
        _aiEvents = aiEvents;
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
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException) { break; }

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
        var users = await (await _usersQueryable()).ToListAsync(ct);

        foreach (var user in users)
        {
            try
            {
                if (!_tzdb.Ids.Contains(user.Timezone)) continue;
                var zone = _tzdb[user.Timezone];
                var nowZoned = SystemClock.Instance.GetCurrentInstant().InZone(zone);
                var todayLocal = nowZoned.Date;

                if (!BirthdayBot.Domain.Utils.DateHelpers.TryParseTimeHHmm(user.NotifyAtLocalTime, out var hh, out var mm))
                    continue;

                if (nowZoned.TimeOfDay.Hour != hh || nowZoned.TimeOfDay.Minute != mm) continue;

                var list = await _birthdays.ListByUserAsync(user.Id, ct);
                foreach (var b in list)
                {
                    var (next, age) = BirthdayBot.Domain.Utils.DateHelpers.NextBirthday(todayLocal, b.Date);
                    var isToday = next == todayLocal;
                    var isTomorrow = next == todayLocal.PlusDays(1);

                    if (!isToday && !isTomorrow) continue;

                    var when = isToday ? (user.Lang == Language.Pl ? "DZIŚ" : user.Lang == Language.Ru ? "СЕГОДНЯ" : "TODAY")
                                       : (user.Lang == Language.Pl ? "JUTRO" : user.Lang == Language.Ru ? "ЗАВТРА" : "TOMORROW");

                    var msg = $"{when}: {b.Name} — {next:yyyy-MM-dd} ({age})";
                    InlineKeyboardMarkup? replyMarkup = null;
                    if (user.AutoGenerateGreetings)
                    {
                        var draft = _greetings.GeneratePersonalized(user, b, age);
                        var enhanceSw = Stopwatch.StartNew();
                        var enhanced = await _enhancer.EnhanceAsync(user, b, draft, age, ct);
                        enhanceSw.Stop();
                        msg += $"\n\n{enhanced.Text}";
                        await _aiEvents.CreateAsync(new AiEvent
                        {
                            UserId = user.Id,
                            TelegramUserId = user.TelegramUserId,
                            EventType = "enhance",
                            InputText = draft,
                            OutputText = enhanced.Text,
                            IsFallback = enhanced.IsFallback,
                            FallbackReason = enhanced.FallbackReason,
                            PromptVersion = enhanced.PromptVersion,
                            ModelSource = enhanced.ModelSource,
                            LatencyMs = enhanceSw.Elapsed.TotalMilliseconds
                        }, ct);
                        replyMarkup = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData(_i18n.GetText(user.Lang, "ai_improve_greeting"), $"ai:improve:{b.Id}")
                            }
                        });
                    }

                    var sent = await _bot.SendTextMessageAsync(
                        chatId: user.TelegramUserId,
                        text: msg,
                        replyMarkup: replyMarkup,
                        cancellationToken: ct);
                    await _logs.CreateAsync(new DeliveryLog
                    {
                        UserId = user.Id,
                        BirthdayId = b.Id, // Теперь b.Id уже ObjectId
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
            var url = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://mongodb:27017/birthdays";
            var dbn = Environment.GetEnvironmentVariable("MONGO_DBNAME") ?? "birthdays";
            var collection = new MongoClient(new MongoUrl(url))
                .GetDatabase(dbn)
                .GetCollection<BirthdayBot.Domain.Entities.User>("users");
            return await collection.FindAsync(_ => true, cancellationToken: ct);
        }
    }
}