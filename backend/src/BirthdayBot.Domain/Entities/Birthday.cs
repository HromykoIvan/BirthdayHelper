using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Domain.Entities;

public class Birthday
{
    [BsonId]
    public ObjectId Id { get; set; }

    public ObjectId UserId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>
    /// Stored as ISO date (no time), assumed local to the person (no TZ).
    /// </summary>
    public DateOnly DateOfBirth { get; set; }

    public List<string> Tags { get; set; } = new();

    public string? Relation { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
