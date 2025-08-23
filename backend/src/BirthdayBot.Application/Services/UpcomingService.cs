using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;

namespace BirthdayBot.Application.Services;

public sealed class UpcomingService : IUpcomingService
{
    public IEnumerable<(Birthday B, DateOnly Occurs, int Turns)>
        InRange(IEnumerable<Birthday> src, DateOnly from, DateOnly to, bool leapToFeb28 = true)
    {
        foreach (var b in src)
        {
            var occurs = NextOccurrence(b.Date, from, leapToFeb28);
            if (occurs > to) continue;
            var turns = occurs.Year - b.Date.Year;
            yield return (b, occurs, turns);
        }
    }

    static DateOnly NextOccurrence(DateOnly birth, DateOnly from, bool leapToFeb28)
    {
        var day = Math.Min(birth.Day, DateTime.DaysInMonth(from.Year, birth.Month));
        var occ = new DateOnly(from.Year, birth.Month, day);

        if (birth.Month == 2 && birth.Day == 29 && !DateTime.IsLeapYear(from.Year))
            occ = leapToFeb28 ? new DateOnly(from.Year, 2, 28) : new DateOnly(from.Year, 3, 1);

        return occ < from ? NextOccurrence(birth, new DateOnly(from.Year + 1, 1, 1), leapToFeb28) : occ;
    }
}