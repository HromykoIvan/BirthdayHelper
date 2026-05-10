namespace BirthdayBot.Application.Interfaces;

public sealed record GoogleImportResult(
    int Imported,
    int Skipped,
    int Total,
    string? Error = null)
{
    public bool IsSuccess => Error is null;
}

public interface IGoogleImportService
{
    /// <summary>Builds a Google OAuth2 authorization URL. The caller stores the state value.</summary>
    string GenerateAuthUrl(string state);

    /// <summary>
    /// Exchanges the authorization code for an access token, fetches contacts with birthdays
    /// from Google People API, saves new records to the database, and returns a summary.
    /// </summary>
    Task<GoogleImportResult> ImportContactsAsync(
        string code,
        long telegramUserId,
        CancellationToken ct = default);
}
