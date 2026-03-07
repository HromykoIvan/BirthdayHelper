using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Domain.Entities;

public sealed class Birthday
{
    [BsonId]
    public ObjectId Id { get; set; }
    
    [BsonRepresentation(BsonType.ObjectId)]
    public ObjectId UserId { get; set; }        // owner

    public string Name { get; set; } = default!;       // first name
    public string? LastName { get; set; }               // last name (optional, for LLM greetings)
    public DateOnly Date { get; set; }

    // Optional metadata for LLM greeting generation
    public string? TimeZoneId { get; set; }     // IANA timezone
    public string? Relation { get; set; }       // "Family/Friend/..."
    public string? Interests { get; set; }      // hobbies/interests for LLM context
    public string? Notes { get; set; }          // free-form notes
    public int?   ReminderDaysBefore { get; set; }

    // Computed properties for fast queries
    public int Month => Date.Month;
    public int Day   => Date.Day;

    /// <summary>Returns "FirstName LastName" or just "FirstName" if no last name.</summary>
    [BsonIgnore]
    public string FullName => string.IsNullOrWhiteSpace(LastName) ? Name : $"{Name} {LastName}";
}
