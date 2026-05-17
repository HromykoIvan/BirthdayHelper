namespace BirthdayBot.Infrastructure.Options;

public sealed class UserRateLimitOptions
{
    public bool Enable { get; set; } = true;
    public int WindowSeconds { get; set; } = 30;
    public int MaxUpdatesPerWindow { get; set; } = 15;
}
