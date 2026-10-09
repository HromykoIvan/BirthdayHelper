namespace BirthdayBot.Application.Models;

/// <summary>
/// Structured birthday/person data extracted from natural-language input.
/// Year is optional because users often know only day and month.
/// </summary>
public sealed record BirthdayDraft(
    string FirstName,
    string? LastName,
    int Day,
    int Month,
    int? Year,
    string? Relation = null,
    string? Profession = null,
    IReadOnlyList<string>? Interests = null,
    string? Notes = null)
{
    public bool HasValidDate =>
        Month is >= 1 and <= 12 &&
        Day >= 1 &&
        Day <= DateTime.DaysInMonth(Year ?? 2000, Month);

    public DateOnly ToDateOnly() =>
        new(Year ?? 2000, Month, Day);

    public string InterestsText =>
        Interests is { Count: > 0 }
            ? string.Join(", ", Interests.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()))
            : string.Empty;
}
