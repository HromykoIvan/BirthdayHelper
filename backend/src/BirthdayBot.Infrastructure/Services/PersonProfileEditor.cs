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

    public static bool TryApply(Birthday person, string field, string input, out string error)
    {
        error = "";
        if (!Fields.Contains(field))
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
        if (clear && (field is "name" or "date" or "memory"))
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
}
