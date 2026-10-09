using NodaTime;

namespace BirthdayBot.Domain.Utils;

public static class DateHelpers
{
    /// <summary>
    /// Returns the next occurrence date in given time zone for a birthday (month/day) from todayDate in that zone.
    /// Also returns the age that will be reached on that next occurrence (or current age if already passed today).
    /// </summary>
    public static (LocalDate nextDate, int turningAge) NextBirthday(LocalDate todayDate, DateOnly dob)
    {
        var targetYear = todayDate.Year;
        var birth = new LocalDate(dob.Year, dob.Month, dob.Day);
        var next = new LocalDate(targetYear, dob.Month, dob.Day);
        if (next < todayDate)
        {
            next = new LocalDate(targetYear + 1, dob.Month, dob.Day);
        }

        var ageOnNext = next.Year - birth.Year;
        return (next, ageOnNext);
    }

    /// <summary>
    /// Returns the next birthday occurrence and a nullable age.
    /// Age is null when the birth year is unknown.
    /// </summary>
    public static (LocalDate nextDate, int? turningAge) NextBirthdayOptionalAge(
        LocalDate todayDate,
        DateOnly dob,
        bool birthYearKnown)
    {
        var targetYear = todayDate.Year;
        var day = Math.Min(dob.Day, DateTime.DaysInMonth(targetYear, dob.Month));
        var next = new LocalDate(targetYear, dob.Month, day);

        if (next < todayDate)
        {
            targetYear++;
            day = Math.Min(dob.Day, DateTime.DaysInMonth(targetYear, dob.Month));
            next = new LocalDate(targetYear, dob.Month, day);
        }

        return (next, birthYearKnown ? next.Year - dob.Year : null);
    }

    public static bool TryParseTimeHHmm(string input, out int hour, out int minute)
    {
        hour = 0; minute = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var parts = input.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out hour)) return false;
        if (!int.TryParse(parts[1], out minute)) return false;
        if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return false;
        return true;
    }
}
