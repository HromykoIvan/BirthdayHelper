using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using BirthdayBot.Domain.Enums;

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

    /// <summary>
    /// False when only day/month are known. Existing records without this field are treated as known-year records.
    /// For unknown-year records Date.Year is a storage sentinel and must not be used to calculate age.
    /// </summary>
    public bool? BirthYearKnown { get; set; }

    [BsonIgnore]
    public bool HasKnownBirthYear => BirthYearKnown ?? true;

    // Optional metadata for LLM greeting generation
    public string? TimeZoneId { get; set; }     // IANA timezone
    public string? Relation { get; set; }       // "Family/Friend/..."
    public string? Interests { get; set; }      // hobbies/interests for LLM context
    public string? Profession { get; set; }     // optional profession/role
    public string? Notes { get; set; }          // free-form notes
    public int?   ReminderDaysBefore { get; set; }
    
    /// <summary>
    /// Language for greeting generation. If null, uses user's interface language.
    /// </summary>
    public Language? GreetingLanguage { get; set; }

    // Computed properties for fast queries
    public int Month => Date.Month;
    public int Day   => Date.Day;

    /// <summary>Returns "FirstName LastName" or just "FirstName" if no last name.</summary>
    [BsonIgnore]
    public string FullName => string.IsNullOrWhiteSpace(LastName) ? Name : $"{Name} {LastName}";
}
