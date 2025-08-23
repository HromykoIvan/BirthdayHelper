// path: backend/src/BirthdayBot.Api/Options/ReminderOptions.cs
namespace BirthdayBot.Api.Options;

public class ReminderOptions
{
    /// <summary>
    /// Cron for hosted service wakeup (default: every minute).
    /// </summary>
    public string Cron { get; set; } = "* * * * *";
}