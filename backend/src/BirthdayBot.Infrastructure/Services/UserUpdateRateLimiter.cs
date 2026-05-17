using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Infrastructure.Services;

public interface IUserUpdateRateLimiter
{
    bool IsAllowed(long telegramUserId);
}

public sealed class UserUpdateRateLimiter : IUserUpdateRateLimiter
{
    private readonly IMemoryCache _cache;
    private readonly UserRateLimitOptions _options;

    public UserUpdateRateLimiter(IMemoryCache cache, IOptions<UserRateLimitOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public bool IsAllowed(long telegramUserId)
    {
        if (!_options.Enable || telegramUserId == 0)
        {
            return true;
        }

        var key = $"usr-rl:{telegramUserId}";
        var current = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(5, _options.WindowSeconds));
            return 0;
        });

        var next = current + 1;
        _cache.Set(key, next, TimeSpan.FromSeconds(Math.Max(5, _options.WindowSeconds)));
        return next <= _options.MaxUpdatesPerWindow;
    }
}
