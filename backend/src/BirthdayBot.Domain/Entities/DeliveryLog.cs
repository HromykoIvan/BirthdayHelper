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

    public string? MessageId { get; set; }

    public string Status { get; set; } = "Sent";

    public string? Error { get; set; }
}
