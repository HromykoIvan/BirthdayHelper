using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Domain.Entities;

public sealed class Birthday
{
    [BsonId]
    public ObjectId Id { get; set; }
    
    [BsonRepresentation(BsonType.ObjectId)]
    public ObjectId UserId { get; set; }        // владелец
    
    public string Name { get; set; } = default!;
    public DateOnly Date { get; set; }

    // НОВОЕ (всё опционально)
    public string? TimeZoneId { get; set; }     // IANA, если хотим хранить именно на запись
    public string? Relation { get; set; }       // «Семья/Друг/...»
    public string? Notes { get; set; }          // произвольные заметки
    public int?   ReminderDaysBefore { get; set; }

    // Для быстрых выборок (необязательно, но полезно)
    public int Month => Date.Month;
    public int Day   => Date.Day;
}