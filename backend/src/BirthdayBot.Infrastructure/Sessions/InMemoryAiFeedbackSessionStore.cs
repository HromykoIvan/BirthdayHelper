using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BirthdayBot.Infrastructure.Sessions;

public sealed class InMemoryAiFeedbackSessionStore : IAiFeedbackSessionStore
{
    private readonly IMemoryCache _cache;
    private const string Prefix = "ai-feedback:";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public InMemoryAiFeedbackSessionStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryGet(long chatId, out AiFeedbackSession session) =>
        _cache.TryGetValue(Prefix + chatId, out session!);

    public void Upsert(AiFeedbackSession session, TimeSpan? ttl = null) =>
        _cache.Set(Prefix + session.ChatId, session, ttl ?? DefaultTtl);

    public bool Remove(long chatId)
    {
        if (_cache.TryGetValue(Prefix + chatId, out _))
        {
            _cache.Remove(Prefix + chatId);
            return true;
        }

        return false;
    }
}
