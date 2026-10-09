using BirthdayBot.Application.Interfaces;
using BirthdayBot.Infrastructure.Options;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Infrastructure.Services;

public sealed class ReminderHostedService : BackgroundService
{
    private readonly ILogger<ReminderHostedService> _logger;
    private readonly IReminderService _reminderService;
    private readonly string _cron;

    public ReminderHostedService(
        ILogger<ReminderHostedService> logger,
        IReminderService reminderService,
        IOptions<ReminderOptions> options)
    {
        _logger = logger;
        _reminderService = reminderService;
        _cron = options.Value.Cron ?? "* * * * *";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var expression = CronExpression.Parse(_cron);

        while (!stoppingToken.IsCancellationRequested)
        {
            var utcNow = DateTimeOffset.UtcNow;
            var next = expression.GetNextOccurrence(utcNow.UtcDateTime, TimeZoneInfo.Utc);
            var delay = next.HasValue
                ? next.Value - utcNow.UtcDateTime
                : TimeSpan.FromMinutes(1);

            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.FromSeconds(10);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                continue;
            }

            try
            {
                await _reminderService.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder run failed");
            }
        }
    }
}
