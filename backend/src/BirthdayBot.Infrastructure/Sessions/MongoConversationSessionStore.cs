using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BirthdayBot.Infrastructure.Sessions;

/// <summary>
/// Mongo-backed wizard session store so Cloud Run cold starts do not lose an unfinished flow.
/// </summary>
public sealed class MongoConversationSessionStore : IConversationSessionStore
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);
    private readonly IMongoCollection<ConversationSessionDocument> _sessions;
    private readonly ILogger<MongoConversationSessionStore> _logger;

    public MongoConversationSessionStore(
        MongoContext context,
        ILogger<MongoConversationSessionStore> logger)
    {
        _sessions = context.ConversationSessions;
        _logger = logger;
    }

    public bool TryGet(long chatId, out AddBirthdayWizardSession session)
    {
        var now = DateTime.UtcNow;
        var doc = _sessions.Find(x => x.ChatId == chatId && x.ExpiresAtUtc > now).FirstOrDefault();
        if (doc is null)
        {
            session = null!;
            return false;
        }

        session = doc.ToModel();
        return true;
    }

    public void Upsert(AddBirthdayWizardSession session, TimeSpan? ttl = null)
    {
        var expiresAt = DateTime.UtcNow.Add(ttl ?? DefaultTtl);
        var doc = ConversationSessionDocument.FromModel(session, expiresAt);

        _sessions.ReplaceOne(
            x => x.ChatId == session.ChatId,
            doc,
            new ReplaceOptions { IsUpsert = true });

        _logger.LogDebug(
            "Persistent wizard session upserted for chat {ChatId}, step {Step}.",
            session.ChatId,
            session.Step);
    }

    public bool Remove(long chatId)
    {
        var result = _sessions.DeleteOne(x => x.ChatId == chatId);
        return result.DeletedCount > 0;
    }
}
