using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Infrastructure.Mongo;

/// <summary>
/// One-time OAuth PKCE state. Never contains access/refresh tokens.
/// A Mongo TTL index also cleans up abandoned authorization flows.
/// </summary>
public sealed class VkAuthorizationSessionDocument
{
    [BsonId]
    public string State { get; set; } = "";
    public long TelegramUserId { get; set; }
    public long ChatId { get; set; }
    public string CodeVerifier { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}
