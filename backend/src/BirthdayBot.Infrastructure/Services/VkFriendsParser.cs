using System.Globalization;
using System.Text.Json;
using BirthdayBot.Application.Services;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Converts VK API friends.get response into minimal birthday-only import entries.
/// Does not retain photos, phone numbers, social IDs, or hidden fields.
/// </summary>
public static class VkFriendsParser
{
    public sealed record Page(
        IReadOnlyList<ContactImportParser.Entry> Entries,
        int TotalFriends,
        int WithoutBirthday,
        int Invalid);

    public static Page Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("error_code", out var n) ? n.GetInt32() : 0;
            // Do not display descriptions returned by VK: they may include request details.
            throw new VkApiException(code);
        }

        if (!root.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("VK returned an invalid friends.get response.");
        }

        var total = response.TryGetProperty("count", out var count)
            ? count.GetInt32() : items.GetArrayLength();

        var entries = new List<ContactImportParser.Entry>();
        var noBirthday = 0;
        var invalid = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("bdate", out var bdate) ||
                bdate.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(bdate.GetString()))
            {
                noBirthday++;
                continue;
            }

            if (!TryParseVkBirthday(bdate.GetString()!, out var date, out var known))
            {
                invalid++;
                continue;
            }

            var first = item.TryGetProperty("first_name", out var firstEl)
                ? firstEl.GetString()?.Trim() : null;
            var last = item.TryGetProperty("last_name", out var lastEl)
                ? lastEl.GetString()?.Trim() : null;

            if (string.IsNullOrWhiteSpace(first) || first.Length > 64 ||
                (last?.Length ?? 0) > 64)
            {
                invalid++;
                continue;
            }

            entries.Add(new ContactImportParser.Entry(first, last, date, known));
        }

        return new Page(entries, total, noBirthday, invalid);
    }

    public static bool TryParseVkBirthday(
        string text, out DateOnly date, out bool yearKnown)
    {
        date = default;
        yearKnown = false;
        var parts = text.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is not (2 or 3) ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month))
        {
            return false;
        }

        yearKnown = parts.Length == 3;
        var year = 2000;
        if (yearKnown && (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
                          year is < 1 or > 9999))
        {
            return false;
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }
}

public sealed class VkApiException : Exception
{
    public int ApiErrorCode { get; }
    public VkApiException(int code) : base($"VK API request failed with code {code}.")
    {
        ApiErrorCode = code;
    }
}
