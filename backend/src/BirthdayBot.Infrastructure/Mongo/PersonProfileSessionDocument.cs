using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Infrastructure.Mongo;

/// <summary>Short-lived conversational edit: data is saved only after the user explicitly selects a field.</summary>
public sealed class PersonProfileSessionDocument
{
    [BsonId]
    public long ChatId { get; set; }
    public long TelegramUserId { get; set; }
    public ObjectId BirthdayId { get; set; }
    public string Field { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
