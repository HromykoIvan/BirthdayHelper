using System.Text.RegularExpressions;
using BirthdayBot.Domain.Entities;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Matches a requested recipient against a user's birthdays. A broad relation ("family")
/// never proves that somebody is the user's mother/father: ambiguous cases require selection.
/// </summary>
public static class BirthdayRecipientMatcher
{
    private static readonly IReadOnlyDictionary<string, string[]> RelationAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["mother"] = ["мама", "маме", "маму", "мамы", "матери", "мать", "мамочка", "mom", "mum", "mother", "mama", "mamie", "mamę", "matka", "matce"],
            ["father"] = ["папа", "папе", "папу", "папы", "отец", "отцу", "отца", "dad", "father", "tata", "tacie", "tatę", "ojciec"],
            ["wife"] = ["жена", "жене", "жену", "жены", "супруга", "супруге", "wife", "żona", "żonie", "żonę"],
            ["husband"] = ["муж", "мужу", "мужа", "супруг", "husband", "mąż", "męża", "mężowi"],
            ["sister"] = ["сестра", "сестре", "сестру", "сестры", "sister", "siostra", "siostrze"],
            ["brother"] = ["брат", "брату", "брата", "brother", "brat", "bratu"],
            ["son"] = ["сын", "сыну", "сына", "son", "syn", "synowi"],
            ["daughter"] = ["дочь", "дочке", "дочку", "дочери", "daughter", "córka", "córce"],
            ["friend"] = ["друг", "друга", "другу", "подруга", "подруге", "friend", "przyjaciel", "przyjaciółka"]
        };

    public static bool IsRelationship(string query) => GetRelationshipKey(query) is not null;

    public static string? GetRelationshipKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim();
        foreach (var (key, aliases) in RelationAliases)
        {
            if (string.Equals(key, cleaned, StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(alias => Regex.IsMatch(
                    cleaned,
                    $@"(?<!\p{{L}}){Regex.Escape(alias)}(?!\p{{L}})",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            {
                return key;
            }
        }

        return null;
    }

    public static IReadOnlyList<Birthday> FindMatches(
        IEnumerable<Birthday> birthdays,
        string query)
    {
        var all = birthdays.ToArray();
        var normalized = query.Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        var byName = all.Where(x =>
            string.Equals(x.FullName, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Name, normalized, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (byName.Length > 0)
        {
            return byName;
        }

        var relation = GetRelationshipKey(normalized);
        if (relation is not null)
        {
            return all.Where(x =>
                string.Equals(GetRelationshipKey(x.Relation), relation, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        // Partial name allowed; never silently collapse two persons into one.
        return all.Where(x => x.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
