using System.Globalization;
using System.Text.RegularExpressions;

namespace BirthdayBot.Application.Utils;

/// <summary>
/// Understands short numeric dates and common Russian, Polish, and English month names.
/// Missing year is represented by a harmless leap-year sentinel; BirthYearKnown must be false.
/// </summary>
public static class BirthdayDateParser
{
    private static readonly Regex Numeric = new(
        @"^(?<day>\d{1,2})[.\-/](?<month>\d{1,2})(?:[.\-/](?<year>\d{4}))?\.?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Named = new(
        @"^(?<day>\d{1,2})\s+(?<month>\p{L}+)(?:\s+(?<year>\d{4}))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, int> Months = BuildMonths();

    public static bool TryParse(string input, out DateOnly date, out bool yearKnown)
    {
        date = default;
        yearKnown = false;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim().TrimEnd('.');
        var match = Numeric.Match(trimmed);
        if (!match.Success)
        {
            match = Named.Match(trimmed);
        }

        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["day"].Value, out var day))
        {
            return false;
        }

        var monthText = match.Groups["month"].Value;
        if (!int.TryParse(monthText, out var month) &&
            !Months.TryGetValue(monthText, out month))
        {
            return false;
        }

        yearKnown = match.Groups["year"].Success;
        var year = yearKnown ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture) : 2000;
        return DateOnly.TryParseExact(
            $"{year:D4}-{month:D2}-{day:D2}",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static Dictionary<string, int> BuildMonths()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in new[] { "ru-RU", "pl-PL", "en-US", "en-GB" })
        {
            var format = CultureInfo.GetCultureInfo(culture).DateTimeFormat;
            for (var month = 1; month <= 12; month++)
            {
                foreach (var name in new[]
                {
                    format.MonthNames[month - 1],
                    format.MonthGenitiveNames[month - 1],
                    format.AbbreviatedMonthNames[month - 1]
                })
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result[name.TrimEnd('.')] = month;
                    }
                }
            }
        }

        return result;
    }
}
