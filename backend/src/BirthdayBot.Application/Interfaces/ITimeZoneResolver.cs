namespace BirthdayBot.Application.Interfaces;

public interface ITimeZoneResolver
{
    bool IsValidTz(string tz);
    Task<string?> FromLocationAsync(double lat, double lon, CancellationToken ct);
    Task<string?> FromCityAsync(string city, CancellationToken ct);

    /// Возвращает IANA TZ ID по названию города, либо null.
    Task<string?> ResolveIanaByCityAsync(string city, CancellationToken ct);
}