using System.Globalization;
using System.Text;
using BirthdayBot.Application.Utils;

namespace BirthdayBot.Application.Services;

/// <summary>
/// Reads contact exports without importing any phone numbers, emails, addresses, or photos.
/// Supports UTF-8 vCard (v3/v4) and Google Contacts CSV.
/// </summary>
public static class ContactImportParser
{
    public const int MaxContacts = 1000;
    public const int MaxFileBytes = 2 * 1024 * 1024;

    public sealed record Entry(string FirstName, string? LastName, DateOnly Birthday, bool YearKnown)
    {
        public string FullName => string.IsNullOrWhiteSpace(LastName)
            ? FirstName
            : $"{FirstName} {LastName}";

        public string DateLabel => YearKnown
            ? Birthday.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : Birthday.ToString("dd.MM", CultureInfo.InvariantCulture);
    }

    public sealed record Result(IReadOnlyList<Entry> Entries, int WithoutBirthday, int Invalid, int Total);

    public static Result Parse(string filename, string content)
    {
        if (content.Length > MaxFileBytes || Encoding.UTF8.GetByteCount(content) > MaxFileBytes)
            throw new ArgumentException("File exceeds the allowed size.");

        var extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension switch
        {
            ".vcf" => ParseVCard(content),
            ".csv" => ParseCsv(content),
            _ => throw new ArgumentException("Only .vcf and .csv files are supported.")
        };
    }

    public static bool TryBirthday(string? value, out DateOnly date, out bool yearKnown)
    {
        date = default;
        yearKnown = false;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim().Trim('"').Trim();
        // vCard RFC 6350: "--MMDD" and "--MM-DD" are dates with no year.
        if (text.StartsWith("--", StringComparison.Ordinal))
        {
            var withoutYear = text[2..].Replace("-", "");
            if (withoutYear.Length == 4 &&
                int.TryParse(withoutYear[..2], out var month) &&
                int.TryParse(withoutYear[2..], out var day))
            {
                return DateOnly.TryParseExact(
                    $"2000-{month:D2}-{day:D2}", "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            }
            return false;
        }

        foreach (var format in new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd" })
        {
            if (DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                yearKnown = true;
                return true;
            }
        }

        if (DateOnly.TryParseExact(
            text,
            new[] { "MMMM d, yyyy", "MMM d, yyyy", "MMMM dd, yyyy", "MMM dd, yyyy" },
            CultureInfo.GetCultureInfo("en-US"),
            DateTimeStyles.None,
            out date))
        {
            yearKnown = true;
            return true;
        }

        // dd.MM, dd.MM.yyyy and named months, not ambiguous numeric MM/DD.
        return BirthdayDateParser.TryParse(text, out date, out yearKnown);
    }

    private static Result ParseVCard(string content)
    {
        var entries = new List<Entry>();
        var withoutDate = 0;
        var invalid = 0;
        var total = 0;
        var inCard = false;
        string? first = null, last = null, fullName = null, birthday = null;

        void CompleteCard()
        {
            if (!inCard) return;
            total++;
            if (string.IsNullOrWhiteSpace(birthday))
            {
                withoutDate++;
                return;
            }

            if (!TryBirthday(birthday, out var date, out var known))
            {
                invalid++;
                return;
            }

            var names = SplitName(fullName);
            var firstName = !string.IsNullOrWhiteSpace(first) ? first.Trim() : names.first;
            var lastName = !string.IsNullOrWhiteSpace(last) ? last.Trim() : names.last;
            if (!ValidName(firstName, lastName))
            {
                invalid++;
                return;
            }

            if (entries.Count >= MaxContacts)
                throw new ArgumentException($"Import supports at most {MaxContacts} birthdays per file.");

            entries.Add(new Entry(firstName, lastName, date, known));
        }

        foreach (var line in UnfoldLines(content))
        {
            if (line.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                inCard = true;
                first = last = fullName = birthday = null;
                continue;
            }

            if (line.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                CompleteCard();
                inCard = false;
                continue;
            }

            if (!inCard) continue;

            var colon = line.IndexOf(':');
            if (colon < 1) continue;
            var propertyName = line[..colon].Split(';')[0].Split('.').Last().Trim();
            var value = line[(colon + 1)..].Trim();

            switch (propertyName.ToUpperInvariant())
            {
                case "N":
                    var components = SplitEscaped(value, ';');
                    last = UnescapeVCard(components.ElementAtOrDefault(0));
                    first = UnescapeVCard(components.ElementAtOrDefault(1));
                    break;
                case "FN":
                    fullName = UnescapeVCard(value);
                    break;
                case "BDAY":
                    birthday = value;
                    break;
            }
        }

        // Ignore trailing incomplete cards.
        return new Result(entries, withoutDate, invalid, total);
    }

    private static Result ParseCsv(string content)
    {
        var rows = ParseCsvRows(content);
        if (rows.Count < 2)
            throw new ArgumentException("CSV must contain a header and at least one contact.");

        var headers = rows[0].Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
        int Column(params string[] names) =>
            Array.FindIndex(headers, x => names.Any(n => x.Equals(n, StringComparison.OrdinalIgnoreCase)));

        var birthIndex = Column("Birthday", "Birthday 1 - Value", "Birthdate", "Date of Birth", "DOB");
        var nameIndex = Column("Name", "Full Name", "Display Name");
        var firstIndex = Column("Given Name", "First Name", "FirstName");
        var lastIndex = Column("Family Name", "Last Name", "Surname", "LastName");

        if (birthIndex < 0 || (nameIndex < 0 && firstIndex < 0))
            throw new ArgumentException("CSV needs a birthday column and Name or Given Name.");

        var entries = new List<Entry>();
        var noBirthday = 0;
        var invalid = 0;
        var total = 0;
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            total++;
            string Value(int i) => i >= 0 && i < row.Length ? row[i].Trim() : "";

            var birth = Value(birthIndex);
            if (string.IsNullOrWhiteSpace(birth))
            {
                noBirthday++;
                continue;
            }

            if (!TryBirthday(birth, out var date, out var yearKnown))
            {
                invalid++;
                continue;
            }

            var full = SplitName(Value(nameIndex));
            var first = !string.IsNullOrWhiteSpace(Value(firstIndex)) ? Value(firstIndex) : full.first;
            var last = !string.IsNullOrWhiteSpace(Value(lastIndex)) ? Value(lastIndex) : full.last;

            if (!ValidName(first, last))
            {
                invalid++;
                continue;
            }

            if (entries.Count >= MaxContacts)
                throw new ArgumentException($"Import supports at most {MaxContacts} birthdays per file.");

            entries.Add(new Entry(first, last, date, yearKnown));
        }

        return new Result(entries, noBirthday, invalid, total);
    }

    private static List<string[]> ParseCsvRows(string content)
    {
        // RFC-4180: commas and newlines inside quoted fields, escaped double quotes.
        var rows = new List<string[]>();
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;

        void EndField()
        {
            fields.Add(value.ToString());
            value.Clear();
        }

        void EndRow()
        {
            EndField();
            rows.Add(fields.ToArray());
            fields.Clear();
        }

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < content.Length && content[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted) EndField();
            else if ((ch == '\r' || ch == '\n') && !quoted)
            {
                EndRow();
                if (ch == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
            }
            else value.Append(ch);
        }

        if (quoted) throw new ArgumentException("Malformed CSV with an unterminated quoted field.");
        if (value.Length > 0 || fields.Count > 0) EndRow();
        return rows;
    }

    private static IEnumerable<string> UnfoldLines(string content)
    {
        string? previous = null;
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && previous is not null)
            {
                previous += line[1..];
            }
            else
            {
                if (previous is not null) yield return previous;
                previous = line.TrimStart('\uFEFF');
            }
        }

        if (previous is not null) yield return previous;
    }

    private static string[] SplitEscaped(string text, char delimiter)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                current.Append(text[i]);
                current.Append(text[++i]);
            }
            else if (text[i] == delimiter)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else current.Append(text[i]);
        }
        parts.Add(current.ToString());
        return parts.ToArray();
    }

    private static string UnescapeVCard(string? value) =>
        (value ?? "").Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
                      .Replace("\\,", ",")
                      .Replace("\\;", ";")
                      .Replace("\\\\", "\\")
                      .Trim();

    private static (string first, string? last) SplitName(string? fullName)
    {
        var trimmed = (fullName ?? "").Trim();
        if (trimmed.Length == 0) return ("", null);
        var parts = trimmed.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return (trimmed, null);
        return (string.Join(' ', parts[..^1]), parts[^1]);
    }

    private static bool ValidName(string? first, string? last) =>
        !string.IsNullOrWhiteSpace(first) && first.Length <= 64 &&
        (last is null || last.Length <= 64);
}
