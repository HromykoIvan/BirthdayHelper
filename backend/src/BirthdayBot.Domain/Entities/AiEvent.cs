using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Domain.Entities;

public sealed class AiEvent
{
    [BsonId]
    public ObjectId Id { get; set; }

    public ObjectId? UserId { get; set; }

    public long? TelegramUserId { get; set; }

    public string EventType { get; set; } = "intent";

    public string? InputText { get; set; }

    public string? OutputText { get; set; }

    public string? ParsedIntent { get; set; }

    public string? ExpectedIntent { get; set; }

    public double? Confidence { get; set; }

    public bool IsFallback { get; set; }

    public string? FallbackReason { get; set; }

    public string PromptVersion { get; set; } = "v1";

    public string? ModelSource { get; set; }

    public double? LatencyMs { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
