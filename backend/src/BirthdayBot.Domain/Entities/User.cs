using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Domain.Entities;

public class User
{
    [BsonId]
    public ObjectId Id { get; set; }

    public long TelegramUserId { get; set; }

    public string Timezone { get; set; } = "Europe/Warsaw";

    /// <summary>
    /// HH:mm in user's local timezone.
    /// </summary>
    public string NotifyAtLocalTime { get; set; } = "09:00";

    public Language Lang { get; set; } = Language.Ru;

    public bool AutoGenerateGreetings { get; set; } = true;

    public Tone Tone { get; set; } = Tone.Friendly;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
