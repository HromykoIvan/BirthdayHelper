namespace BirthdayBot.Application.Models;

public enum AddWizardStep { Name, LastName, Date, TimeZone, Relation, Interests, Confirm }

public sealed class AddBirthdayWizardSession
{
    public long ChatId { get; }
    public long UserId { get; }

    public AddWizardStep Step { get; set; } = AddWizardStep.Name;
    public string? Name { get; set; }
    public string? LastName { get; set; }
    public DateOnly? Date { get; set; }

    public string? TimeZoneId { get; set; }
    public string? Relation { get; set; }
    public string? Interests { get; set; }

    public bool WaitingCity { get; set; }     // waiting for city text input

    /// <summary>
    /// MessageId of the calendar message so we can edit it when navigating months.
    /// </summary>
    public int? CalendarMessageId { get; set; }

    public AddBirthdayWizardSession(long chatId, long userId)
    { ChatId = chatId; UserId = userId; }
}
