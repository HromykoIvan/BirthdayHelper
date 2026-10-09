namespace BirthdayBot.Infrastructure.Options;

public class ReminderOptions
{
    /// <summary>
    /// Cron schedule used by the legacy in-process reminder worker.
    /// </summary>
    public string Cron { get; set; } = "* * * * *";

    /// <summary>
    /// Keep true for local/legacy hosting. Cloud Run sets this to false
    /// and invokes reminders externally through Cloud Scheduler.
    /// </summary>
    public bool RunAsHostedService { get; set; } = true;
}
