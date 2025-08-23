using System.Text;
using System.Globalization;

namespace BirthdayBot.Application.Utils;

public static class Formatting
{
    public static string Html(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string BirthdayCard(string name, DateOnly birth, DateOnly occurs, int turns)
        => $"• <b>{Html(name)}</b> — {turns} {PluralYears(turns)}  (<code>{birth:yyyy-MM-dd}</code>)";

    public static string PluralYears(int n)
        => (n % 10 == 1 && n % 100 != 11) ? "год" :
           (n % 10 is >= 2 and <= 4 && (n % 100 < 10 || n % 100 >= 20)) ? "года" : "лет";
}