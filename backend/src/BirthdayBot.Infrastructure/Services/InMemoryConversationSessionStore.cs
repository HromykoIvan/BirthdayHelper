using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BirthdayBot.Infrastructure.Sessions;

/// <summary>
/// Простое in-memory хранилище с TTL. Для продакшна можно заменить на Redis, не меняя интерфейс.
/// </summary>
public sealed class InMemoryConversationSessionStore : IConversationSessionStore
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryConversationSessionStore> _logger;

    // Не стрингуем chatId напрямую — добавим префикс, чтобы ключи были уникальны.
    private const string Prefix = "wizard:add-birthday:";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public InMemoryConversationSessionStore(IMemoryCache cache, ILogger<InMemoryConversationSessionStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public bool TryGet(long chatId, out AddBirthdayWizardSession session)
        => _cache.TryGetValue(Prefix + chatId, out session!);

    public void Upsert(AddBirthdayWizardSession session, TimeSpan? ttl = null)
    {
        _cache.Set(Prefix + session.ChatId, session, ttl ?? DefaultTtl);
        _logger.LogDebug("Wizard session upserted for chat {ChatId}, step: {Step}", session.ChatId, session.Step);
    }

    public bool Remove(long chatId)
    {
        if (_cache.TryGetValue(Prefix + chatId, out _))
        {
            _cache.Remove(Prefix + chatId);
            _logger.LogDebug("Wizard session removed for chat {ChatId}", chatId);
            return true;
        }
        return false;
    }
}