// path: backend/src/BirthdayBot.Infrastructure/Options/ReminderOptions.cs
namespace BirthdayBot.Infrastructure.Options;

public class ReminderOptions
{
    /// <summary>
    /// Cron для запуска фонового сервиса (по умолчанию: каждую минуту).
    /// </summary>
    public string Cron { get; set; } = "* * * * *";
}