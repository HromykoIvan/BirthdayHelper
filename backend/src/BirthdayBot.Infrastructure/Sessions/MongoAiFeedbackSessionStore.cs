using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Infrastructure.Mongo;
using MongoDB.Driver;

namespace BirthdayBot.Infrastructure.Sessions;

/// <summary>
/// Mongo-backed short-lived state used while a user is typing feedback for a generated greeting.
/// </summary>
public sealed class MongoAiFeedbackSessionStore : IAiFeedbackSessionStore
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);
    private readonly IMongoCollection<AiFeedbackSessionDocument> _sessions;

    public MongoAiFeedbackSessionStore(MongoContext context)
    {
        _sessions = context.AiFeedbackSessions;
    }

    public bool TryGet(long chatId, out AiFeedbackSession session)
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

    public void Upsert(AiFeedbackSession session, TimeSpan? ttl = null)
    {
        var doc = new AiFeedbackSessionDocument
        {
            ChatId = session.ChatId,
            SourceEventId = session.SourceEventId,
            ExpiresAtUtc = DateTime.UtcNow.Add(ttl ?? DefaultTtl)
        };

        _sessions.ReplaceOne(
            x => x.ChatId == session.ChatId,
            doc,
            new ReplaceOptions { IsUpsert = true });
    }

    public bool Remove(long chatId)
    {
        var result = _sessions.DeleteOne(x => x.ChatId == chatId);
        return result.DeletedCount > 0;
    }
}
