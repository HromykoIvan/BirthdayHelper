using System.Globalization;
using System.Net.Http.Json;
using BirthdayBot.Application.Interfaces;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace BirthdayBot.Infrastructure.Geo;

public sealed class TimeZoneResolver : ITimeZoneResolver
{
    private readonly HttpClient _http;
    private readonly ILogger<TimeZoneResolver> _log;

    public TimeZoneResolver(HttpClient http, ILogger<TimeZoneResolver> log)
    { _http = http; _log = log; }

    public bool IsValidTz(string tz)
        => DateTimeZoneProviders.Tzdb.GetZoneOrNull(tz) is not null;

    public Task<string?> FromLocationAsync(double lat, double lon, CancellationToken ct)
    {
        var tz = GeoTimeZone.TimeZoneLookup.GetTimeZone(lat, lon).Result;
        return Task.FromResult(IsValidTz(tz) ? tz : null);
    }

    public async Task<string?> FromCityAsync(string city, CancellationToken ct)
    {
        try
        {
            var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={Uri.EscapeDataString(city)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("BirthdayBot/1.0 (+contact@example.com)");
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var arr = await resp.Content.ReadFromJsonAsync<List<NominatimItem>>(cancellationToken: ct);
            var p = arr?.FirstOrDefault();
            if (p is null) return null;

            if (!double.TryParse(p.lat, CultureInfo.InvariantCulture, out var lat)) return null;
            if (!double.TryParse(p.lon, CultureInfo.InvariantCulture, out var lon)) return null;

            var tz = GeoTimeZone.TimeZoneLookup.GetTimeZone(lat, lon).Result;
            return IsValidTz(tz) ? tz : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "FromCity failed for {City}", city);
            return null;
        }
    }

    private sealed record NominatimItem(string lat, string lon);
}