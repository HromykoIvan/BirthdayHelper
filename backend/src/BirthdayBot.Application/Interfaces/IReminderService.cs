namespace BirthdayBot.Application.Interfaces;

public interface IReminderService
{
    Task RunOnceAsync(CancellationToken ct);
}
