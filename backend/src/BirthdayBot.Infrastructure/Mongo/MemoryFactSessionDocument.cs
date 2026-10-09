using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Infrastructure.Mongo;

/// <summary>
/// One pending user-supplied fact per chat. Expires in ten minutes; nothing changes
/// on a Birthday document until the owner explicitly taps "Save".
/// </summary>
public sealed class MemoryFactSessionDocument
{
    [BsonId]
    public long ChatId { get; set; }
    public long TelegramUserId { get; set; }
    public ObjectId OwnerId { get; set; }
    public List<ObjectId> CandidateIds { get; set; } = [];
    public ObjectId? SelectedPersonId { get; set; }
    public string Field { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
