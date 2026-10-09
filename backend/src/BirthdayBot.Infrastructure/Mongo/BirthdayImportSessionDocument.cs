using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Infrastructure.Mongo;

public sealed class BirthdayImportCandidate
{
    public int Index { get; set; }
    public string FirstName { get; set; } = "";
    public string? LastName { get; set; }
    public DateOnly Date { get; set; }
    public bool YearKnown { get; set; }
    public bool Selected { get; set; }

    // New, Duplicate (same name and day/month), or Conflict (same name but different birthday).
    public string Status { get; set; } = "New";

    [BsonIgnore]
    public string FullName => string.IsNullOrWhiteSpace(LastName)
        ? FirstName
        : $"{FirstName} {LastName}";
}

public sealed class BirthdayImportSessionDocument
{
    [BsonId]
    public long ChatId { get; set; }
    public ObjectId UserId { get; set; }
    public string Source { get; set; } = "";
    public string Status { get; set; } = "Preview";
    public List<BirthdayImportCandidate> Candidates { get; set; } = [];
    public int WithoutBirthday { get; set; }
    public int Invalid { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
