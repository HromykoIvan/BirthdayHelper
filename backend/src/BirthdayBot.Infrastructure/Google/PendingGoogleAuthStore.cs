using Microsoft.Extensions.Caching.Memory;

namespace BirthdayBot.Infrastructure.Google;

/// <summary>
/// Short-lived store that maps an OAuth state token to the originating Telegram user.
/// Entries expire after 10 minutes — long enough for a user to complete the OAuth flow.
/// </summary>
public sealed class PendingGoogleAuthStore
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const string Prefix = "google:oauth:state:";

    public PendingGoogleAuthStore(IMemoryCache cache) => _cache = cache;

    public void Set(string state, PendingGoogleAuthEntry entry) =>
        _cache.Set(Prefix + state, entry, Ttl);

    public bool TryGet(string state, out PendingGoogleAuthEntry entry) =>
        _cache.TryGetValue(Prefix + state, out entry!);

    public void Remove(string state) =>
        _cache.Remove(Prefix + state);
}

public sealed record PendingGoogleAuthEntry(long TelegramUserId, long ChatId);
