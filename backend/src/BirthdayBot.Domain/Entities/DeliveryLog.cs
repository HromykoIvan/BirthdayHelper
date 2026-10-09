using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Domain.Entities;

public class DeliveryLog
{
    [BsonId]
    public ObjectId Id { get; set; }

    public ObjectId UserId { get; set; }

    public ObjectId BirthdayId { get; set; }

    public DateTime WhenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Stable key for one logical reminder delivery. Used to make Cloud Scheduler retries idempotent.
    /// </summary>
    public string? DeliveryKey { get; set; }

    public int? DaysBefore { get; set; }

    public string? MessageId { get; set; }

    public string Status { get; set; } = "Sent";

    public string? Error { get; set; }
}
