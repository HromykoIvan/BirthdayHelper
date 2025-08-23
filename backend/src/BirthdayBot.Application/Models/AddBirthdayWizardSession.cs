namespace BirthdayBot.Application.Models;

public enum AddWizardStep { Name, Date, TimeZone, Relation, Confirm }

public sealed class AddBirthdayWizardSession
{
    public long ChatId { get; }
    public long UserId { get; }

    public AddWizardStep Step { get; set; } = AddWizardStep.Name;
    public string? Name { get; set; }
    public DateOnly? Date { get; set; }

    public string? TimeZoneId { get; set; }   // новое
    public string? Relation { get; set; }     // новое

    public bool WaitingCity { get; set; }     // ждём ввод города текстом

    public AddBirthdayWizardSession(long chatId, long userId)
    { ChatId = chatId; UserId = userId; }
}