using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Application.Models;

public enum AddWizardStep { Name, LastName, Date, TimeZone, Relation, Interests, GreetingLanguage, Confirm }

/// <summary>Which sub-step of the date form the user is on.</summary>
public enum DatePickerPhase { Day, Month, Year }

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
    public Language? GreetingLanguage { get; set; }

    public bool WaitingCity { get; set; }

    // ── Date picker state ──────────────────────────────────────────────────
    /// <summary>Id of the "date form" message so we can edit it in place.</summary>
    public int? CalendarMessageId { get; set; }

    /// <summary>Day (1-31) selected during the date-picker flow.</summary>
    public int? DatePickerDay { get; set; }

    /// <summary>Month (1-12) selected during the date-picker flow.</summary>
    public int? DatePickerMonth { get; set; }

    /// <summary>Current sub-step of the date picker.</summary>
    public DatePickerPhase DatePhase { get; set; } = DatePickerPhase.Day;

    /// <summary>Current page of the year picker (0 = most recent years).</summary>
    public int YearPage { get; set; } = 0;

    public AddBirthdayWizardSession(long chatId, long userId)
    { ChatId = chatId; UserId = userId; }
}
