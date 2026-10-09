using BirthdayBot.Application.Utils;
using BirthdayBot.Domain.Entities;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>Validates profile changes before they reach MongoDB. A failed edit never mutates a profile.</summary>
public static class PersonProfileEditor
{
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "date", "relation", "interests", "profession", "gifts", "notes", "memory"
    };

    private static readonly IReadOnlySet<string> AppendFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "append_interests", "append_gifts"
    };

    public static bool TryApply(Birthday person, string field, string input, out string error)
    {
        error = "";
        if (!Fields.Contains(field) && !AppendFields.Contains(field))
        {
            error = "unknown";
            return false;
        }

        var value = input.Trim();
        if (value.Length == 0)
        {
            error = "empty";
            return false;
        }

        // A dash clears optional fields. It must never erase the person's name or birthday.
        var clear = value is "-" or "—";
        if (clear && (field is "name" or "date" or "memory" or "append_interests" or "append_gifts"))
        {
            error = "empty";
            return false;
        }

        var maxLength = field switch
        {
            "name" => 128,
            "date" => 48,
            "memory" => 500,
            "notes" => 2000,
            _ => 500
        };
        if (value.Length > maxLength)
        {
            error = "too_long";
            return false;
        }

        switch (field)
        {
            case "name":
                var parts = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts[0].Length > 64 || (parts.Length == 2 && parts[1].Length > 64))
                {
                    error = "too_long";
                    return false;
                }
                person.Name = parts[0];
                person.LastName = parts.Length == 2 ? parts[1] : null;
                break;

            case "date":
                if (!BirthdayDateParser.TryParse(value, out var date, out var yearKnown))
                {
                    error = "invalid_date";
                    return false;
                }
                person.Date = date;
                person.BirthYearKnown = yearKnown;
                break;

            case "relation": person.Relation = clear ? null : value; break;
            case "interests": person.Interests = clear ? null : value; break;
            case "profession": person.Profession = clear ? null : value; break;
            case "gifts": person.GiftIdeas = clear ? null : value; break;
            case "notes": person.Notes = clear ? null : value; break;
            case "append_interests":
                return TryAppend(person.Interests, value, 2000,
                    next => person.Interests = next, out error);
            case "append_gifts":
                return TryAppend(person.GiftIdeas, value, 2000,
                    next => person.GiftIdeas = next, out error);

            case "memory":
                var appended = string.IsNullOrWhiteSpace(person.Notes)
                    ? "• " + value
                    : person.Notes + "\n• " + value;
                if (appended.Length > 4000)
                {
                    error = "notes_full";
                    return false;
                }
                person.Notes = appended;
                break;
        }

        return true;
    }
    private static bool TryAppend(string? existing, string value, int limit,
        Action<string> apply, out string error)
    {
        error = "";
        var values = (existing ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (values.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            return true; // Idempotent: do not repeat an interest/gift preference.

        var combined = string.IsNullOrWhiteSpace(existing) ? value : existing.Trim() + "; " + value;
        if (combined.Length > limit)
        {
            error = "notes_full";
            return false;
        }
        apply(combined);
        return true;
    }

}
