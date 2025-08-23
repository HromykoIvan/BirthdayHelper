using BirthdayBot.Domain.Entities;

namespace BirthdayBot.Application.Interfaces;

public interface IUpcomingService
{
    IEnumerable<(Birthday B, DateOnly Occurs, int Turns)>
        InRange(IEnumerable<Birthday> src, DateOnly from, DateOnly to, bool leapToFeb28 = true);
}