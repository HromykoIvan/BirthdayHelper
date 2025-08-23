namespace BirthdayBot.Application.Interfaces;

public interface ITimeZoneResolver
{
    bool IsValidTz(string tz);
    Task<string?> FromLocationAsync(double lat, double lon, CancellationToken ct);
    Task<string?> FromCityAsync(string city, CancellationToken ct);
}