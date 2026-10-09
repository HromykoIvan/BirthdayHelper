using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.UI;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Utils;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Infrastructure.Services;

public sealed class ReminderService : IReminderService
{
    private static readonly int[] DefaultReminderScheduleDays = [7, 1, 0];

    private readonly ILogger<ReminderService> _logger;
    private readonly IUserRepository _users;
    private readonly IBirthdayRepository _birthdays;
    private readonly IDeliveryLogRepository _logs;
    private readonly IGreetingGenerator _greetings;
    private readonly ILocalizationService _i18n;
    private readonly ITelegramBotClient _bot;
    private readonly IAiGreetingEnhancer _enhancer;
    private readonly IAiEventRepository _aiEvents;
    private readonly IDateTimeZoneProvider _tzdb = DateTimeZoneProviders.Tzdb;

    public ReminderService(
        ILogger<ReminderService> logger,
        IUserRepository users,
        IBirthdayRepository birthdays,
        IDeliveryLogRepository logs,
        IGreetingGenerator greetings,
        IAiGreetingEnhancer enhancer,
        IAiEventRepository aiEvents,
        ILocalizationService i18n,
        ITelegramBotClient bot)
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
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var users = await _users.ListAllAsync(ct);

        foreach (var user in users)
        {
            try
            {
                await ProcessUserAsync(user, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder error for user {User}", user.TelegramUserId);
            }
        }
    }

    private async Task ProcessUserAsync(User user, CancellationToken ct)
    {
        if (!_tzdb.Ids.Contains(user.Timezone))
        {
            return;
        }

        var zone = _tzdb[user.Timezone];
        var nowZoned = SystemClock.Instance.GetCurrentInstant().InZone(zone);
        var todayLocal = nowZoned.Date;

        if (!DateHelpers.TryParseTimeHHmm(user.NotifyAtLocalTime, out var hh, out var mm))
        {
            return;
        }

        // Cloud Scheduler calls once per minute. Only act during the user's selected minute.
        if (nowZoned.TimeOfDay.Hour != hh || nowZoned.TimeOfDay.Minute != mm)
        {
            return;
        }

        var list = await _birthdays.ListByUserAsync(user.Id, ct);
        foreach (var birthday in list)
        {
            try
            {
                await ProcessBirthdayAsync(user, birthday, todayLocal, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Reminder error for user {User}, birthday {BirthdayId}",
                    user.TelegramUserId,
                    birthday.Id);
            }
        }
    }

    private async Task ProcessBirthdayAsync(
        User user,
        Birthday birthday,
        LocalDate todayLocal,
        CancellationToken ct)
    {
        var (next, age) = DateHelpers.NextBirthdayOptionalAge(
            todayLocal,
            birthday.Date,
            birthday.HasKnownBirthYear);

        var daysBefore = Period.Between(todayLocal, next, PeriodUnits.Days).Days;
        var schedule = GetReminderSchedule(birthday);
        if (!schedule.Contains(daysBefore))
        {
            return;
        }

        // Reserve this logical delivery before calling AI or Telegram.
        // The unique sparse index on DeliveryKey makes Scheduler retries idempotent.
        var delivery = new DeliveryLog
        {
            UserId = user.Id,
            BirthdayId = birthday.Id,
            WhenUtc = DateTime.UtcNow,
            DaysBefore = daysBefore,
            DeliveryKey = $"{user.Id}:{birthday.Id}:{todayLocal:yyyyMMdd}:{daysBefore}",
            Status = "Pending"
        };

        if (!await _logs.TryCreateAsync(delivery, ct))
        {
            _logger.LogDebug(
                "Skipping duplicate reminder delivery {DeliveryKey}.",
                delivery.DeliveryKey);
            return;
        }

        try
        {
            var when = FormatWhen(user.Lang, daysBefore);
            var ageSuffix = age.HasValue ? $" ({age.Value})" : "";
            var date = birthday.HasKnownBirthYear
                ? $"{birthday.Date:dd.MM.yyyy}"
                : $"{birthday.Date:dd.MM}";

            var message = $"🎂 {when}: {birthday.FullName}\n📅 {date}{ageSuffix}";
            InlineKeyboardMarkup? replyMarkup = null;

            if (daysBefore > 0)
            {
                message += user.Lang switch
                {
                    Language.Ru => "\n\nМожно заранее подготовить поздравление.",
                    Language.Pl => "\n\nMożesz wcześniej przygotować życzenia.",
                    _ => "\n\nYou can prepare a greeting in advance."
                };
            }

            if (user.AutoGenerateGreetings)
            {
                var draft = _greetings.GeneratePersonalized(user, birthday, age);
                var enhanceSw = Stopwatch.StartNew();
                var enhanced = await _enhancer.EnhanceAsync(user, birthday, draft, age, ct);
                enhanceSw.Stop();

                message += $"\n\n{enhanced.Text}";

                var aiEvent = await _aiEvents.CreateAsync(new AiEvent
                {
                    UserId = user.Id,
                    TelegramUserId = user.TelegramUserId,
                    BirthdayId = birthday.Id,
                    EventType = "enhance_reminder",
                    InputText = draft,
                    OutputText = enhanced.Text,
                    OutputVariants = enhanced.Variants?.ToDictionary(x => x.Style, x => x.Text),
                    IsFallback = enhanced.IsFallback,
                    FallbackReason = enhanced.FallbackReason,
                    PromptVersion = enhanced.PromptVersion,
                    ModelSource = enhanced.ModelSource,
                    InputTokens = enhanced.InputTokens,
                    OutputTokens = enhanced.OutputTokens,
                    LatencyMs = enhanceSw.Elapsed.TotalMilliseconds
                }, ct);

                replyMarkup = Keyboards.ReminderGreetingActionsKb(
                    user.Lang,
                    aiEvent.Id.ToString(),
                    enhanced.Variants is { Count: > 0 });
            }

            var sent = await _bot.SendTextMessageAsync(
                chatId: user.TelegramUserId,
                text: message,
                replyMarkup: replyMarkup,
                cancellationToken: ct);

            await _logs.UpdateStatusAsync(
                delivery.Id,
                status: "Sent",
                messageId: sent.MessageId.ToString(),
                ct: ct);
        }
        catch (Exception ex)
        {
            // Delete the reservation so the Scheduler retry can attempt the delivery again.
            await _logs.DeleteAsync(delivery.Id, CancellationToken.None);
            _logger.LogWarning(
                ex,
                "Reminder delivery failed; reservation {DeliveryKey} was released for retry.",
                delivery.DeliveryKey);
            throw;
        }
    }

    private static IReadOnlyCollection<int> GetReminderSchedule(Birthday birthday)
    {
        if (birthday.ReminderDaysBefore is { } custom)
        {
            return new[] { Math.Max(custom, 0), 0 }
                .Distinct()
                .OrderByDescending(x => x)
                .ToArray();
        }

        return DefaultReminderScheduleDays;
    }

    private static string FormatWhen(Language language, int daysBefore) =>
        language switch
        {
            Language.Ru => daysBefore switch
            {
                0 => "СЕГОДНЯ",
                1 => "ЗАВТРА",
                _ => $"ЧЕРЕЗ {daysBefore} ДН."
            },
            Language.Pl => daysBefore switch
            {
                0 => "DZISIAJ",
                1 => "JUTRO",
                _ => $"ZA {daysBefore} DNI"
            },
            _ => daysBefore switch
            {
                0 => "TODAY",
                1 => "TOMORROW",
                _ => $"IN {daysBefore} DAYS"
            }
        };
}
