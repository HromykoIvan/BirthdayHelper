using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace BirthdayBot.Infrastructure.Google;

public sealed class GoogleImportService : IGoogleImportService
{
    private readonly HttpClient _http;
    private readonly GoogleOptions _opts;
    private readonly IUserRepository _users;
    private readonly IBirthdayRepository _birthdays;
    private readonly ILogger<GoogleImportService> _log;

    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string PeopleEndpoint =
        "https://people.googleapis.com/v1/people/me/connections"
        + "?personFields=names,birthdays,relations&pageSize=1000";

    private const string Scope = "https://www.googleapis.com/auth/contacts.readonly";

    public GoogleImportService(
        HttpClient http,
        IOptions<GoogleOptions> opts,
        IUserRepository users,
        IBirthdayRepository birthdays,
        ILogger<GoogleImportService> log)
    {
        _http = http;
        _opts = opts.Value;
        _users = users;
        _birthdays = birthdays;
        _log = log;
    }

    public string GenerateAuthUrl(string state)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["client_id"] = _opts.ClientId;
        q["redirect_uri"] = _opts.CallbackUrl;
        q["response_type"] = "code";
        q["scope"] = Scope;
        q["access_type"] = "online";
        q["state"] = state;
        q["prompt"] = "select_account";
        return "https://accounts.google.com/o/oauth2/v2/auth?" + q;
    }

    public async Task<GoogleImportResult> ImportContactsAsync(
        string code,
        long telegramUserId,
        CancellationToken ct = default)
    {
        // 1. Exchange code for access token
        string accessToken;
        try
        {
            accessToken = await ExchangeCodeAsync(code, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Google token exchange failed for user {UserId}", telegramUserId);
            return new GoogleImportResult(0, 0, 0, Error: "token_exchange_failed");
        }

        // 2. Fetch user from DB (or create on the fly if somehow missing)
        var user = await _users.GetByTelegramUserIdAsync(telegramUserId, ct);
        if (user is null)
        {
            _log.LogWarning("Google import: user {TelegramId} not found in DB", telegramUserId);
            return new GoogleImportResult(0, 0, 0, Error: "user_not_found");
        }

        // 3. Load contacts with birthdays from People API (paginated)
        List<GoogleContact> contacts;
        try
        {
            contacts = await FetchContactsAsync(accessToken, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Google People API call failed for user {UserId}", telegramUserId);
            return new GoogleImportResult(0, 0, 0, Error: "people_api_failed");
        }

        // 4. Filter to contacts that have at least a name and a birthday
        var withBirthday = contacts
            .Where(c => c.Names?.Count > 0 && c.Birthdays?.Count > 0)
            .ToList();

        _log.LogInformation(
            "Google import: user {UserId} — {Total} contacts, {WithBirthday} with birthday",
            telegramUserId, contacts.Count, withBirthday.Count);

        // 5. Import, deduplicating by (Name, Month, Day)
        int imported = 0, skipped = 0;
        foreach (var contact in withBirthday)
        {
            try
            {
                var (imp, sk) = await ImportContactAsync(contact, user, ct);
                imported += imp;
                skipped += sk;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to import contact '{Name}'",
                    contact.Names?.FirstOrDefault()?.DisplayName ?? "?");
                skipped++;
            }
        }

        return new GoogleImportResult(imported, skipped, withBirthday.Count);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<string> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["redirect_uri"]  = _opts.CallbackUrl,
            ["grant_type"]    = "authorization_code"
        });

        var resp = await _http.PostAsync(TokenEndpoint, form, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange HTTP {resp.StatusCode}: {json}");

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(json)
            ?? throw new InvalidOperationException("Empty token response");

        if (string.IsNullOrEmpty(token.AccessToken))
            throw new InvalidOperationException("Access token missing in response");

        return token.AccessToken;
    }

    private async Task<List<GoogleContact>> FetchContactsAsync(string accessToken, CancellationToken ct)
    {
        var all = new List<GoogleContact>();
        string? nextPageToken = null;

        do
        {
            var url = nextPageToken is null
                ? PeopleEndpoint
                : PeopleEndpoint + "&pageToken=" + Uri.EscapeDataString(nextPageToken);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"People API HTTP {resp.StatusCode}: {json}");

            var page = JsonSerializer.Deserialize<GoogleConnectionsResponse>(json);
            if (page?.Connections is { Count: > 0 } connections)
                all.AddRange(connections);

            nextPageToken = page?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(nextPageToken));

        return all;
    }

    private async Task<(int imported, int skipped)> ImportContactAsync(
        GoogleContact contact,
        BirthdayBot.Domain.Entities.User user,
        CancellationToken ct)
    {
        var firstName = contact.Names!.FirstOrDefault()?.GivenName?.Trim()
                     ?? contact.Names.First().DisplayName?.Split(' ').FirstOrDefault()?.Trim()
                     ?? string.Empty;

        if (string.IsNullOrEmpty(firstName))
            return (0, 1);

        var gBirthday = contact.Birthdays!.First();
        if (gBirthday.Date is not { } bd || bd.Month is null || bd.Day is null)
            return (0, 1); // no usable date

        var month = bd.Month.Value;
        var day   = bd.Day.Value;
        var year  = bd.Year ?? 1;  // year 1 = "year unknown"

        DateOnly date;
        try { date = new DateOnly(year, month, day); }
        catch { return (0, 1); }

        // Deduplicate: same first-name + month + day already exists?
        var existing = await _birthdays.FindByNameAsync(user.Id, firstName, ct);
        if (existing is not null
            && existing.Date.Month == month
            && existing.Date.Day   == day)
            return (0, 1);

        var lastName = contact.Names.FirstOrDefault()?.FamilyName?.Trim();
        var relation = contact.Relations?.FirstOrDefault()?.Type?.Trim();

        await _birthdays.CreateAsync(new BirthdayBot.Domain.Entities.Birthday
        {
            Id       = ObjectId.GenerateNewId(),
            UserId   = user.Id,
            Name     = firstName,
            LastName = string.IsNullOrEmpty(lastName) ? null : lastName,
            Date     = date,
            Relation = string.IsNullOrEmpty(relation) ? null : relation,
            TimeZoneId = user.Timezone,
        }, ct);

        return (1, 0);
    }

    // ── Google API response models ───────────────────────────────────────────

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }

    private sealed class GoogleConnectionsResponse
    {
        [JsonPropertyName("connections")] public List<GoogleContact>? Connections { get; set; }
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    }

    private sealed class GoogleContact
    {
        [JsonPropertyName("names")]      public List<GoogleName>?     Names      { get; set; }
        [JsonPropertyName("birthdays")]  public List<GoogleBirthday>? Birthdays  { get; set; }
        [JsonPropertyName("relations")]  public List<GoogleRelation>? Relations  { get; set; }
    }

    private sealed class GoogleName
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("givenName")]   public string? GivenName   { get; set; }
        [JsonPropertyName("familyName")]  public string? FamilyName  { get; set; }
    }

    private sealed class GoogleBirthday
    {
        [JsonPropertyName("date")] public GoogleDate? Date { get; set; }
    }

    private sealed class GoogleDate
    {
        [JsonPropertyName("year")]  public int? Year  { get; set; }
        [JsonPropertyName("month")] public int? Month { get; set; }
        [JsonPropertyName("day")]   public int? Day   { get; set; }
    }

    private sealed class GoogleRelation
    {
        [JsonPropertyName("type")]   public string? Type   { get; set; }
        [JsonPropertyName("person")] public string? Person { get; set; }
    }
}
