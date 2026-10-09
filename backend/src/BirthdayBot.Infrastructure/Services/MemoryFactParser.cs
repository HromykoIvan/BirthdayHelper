using System.Text.RegularExpressions;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Deliberately conservative parser for volunteered facts. Does not turn general conversation,
/// questions, or AI guesses into stored data. Unrecognized messages use the existing intent router.
/// </summary>
public static class MemoryFactParser
{
    private static readonly Regex Statement = new(
        @"^(?<subject>[\p{L}\p{M}'’\-]+(?:\s+[\p{L}\p{M}'’\-]+){0,2}?)\s+" +
        @"(?<verb>понравилась\s+идея\s+подарить|понравился\s+подарок|мечтает\s+о|" +
        @"не\s+любит|не\s+нравится|любит|обожает|увлекается|хочет|" +
        @"nie\s+lubi|uwielbia|marzy\s+o|lubi|chce|" +
        @"doesn['’]t\s+like|dreams\s+of|likes|loves|enjoys|wants)\s+" +
        @"(?<detail>[^?]+?)[.!]?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(150));

    private static readonly HashSet<string> Interests = new(StringComparer.OrdinalIgnoreCase)
    {
        "любит", "обожает", "увлекается",
        "lubi", "uwielbia", "likes", "loves", "enjoys"
    };

    private static readonly HashSet<string> Gifts = new(StringComparer.OrdinalIgnoreCase)
    {
        "понравилась идея подарить", "понравился подарок",
        "хочет", "chce", "wants"
    };

    public static bool TryParse(string? message, out MemoryFact? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var input = message.Trim();
        if (input.Length is < 8 or > 400 || input.StartsWith('/') || input.Contains('\n'))
            return false;

        var match = Statement.Match(input);
        if (!match.Success)
            return false;

        var subject = Regex.Replace(match.Groups["subject"].Value.Trim(), @"\s+", " ");
        var verb = Regex.Replace(match.Groups["verb"].Value.Trim(), @"\s+", " ").ToLowerInvariant();
        var detail = match.Groups["detail"].Value.Trim().TrimEnd('.', '!', ' ');
        if (detail.Length is < 2 or > 240)
            return false;

        // Do not treat negation as a positive interest. Store the original meaning as a note.
        var field = Interests.Contains(verb) ? "append_interests"
            : Gifts.Contains(verb) ? "append_gifts"
            : "memory";
        var value = field == "memory" ? $"{verb} {detail}" : detail;
        result = new MemoryFact(subject, field, value);
        return true;
    }
}

public sealed record MemoryFact(string Subject, string Field, string Value);
