using MongoDB.Bson;

namespace BirthdayBot.Application.Models;

public sealed class AiFeedbackSession
{
    public long ChatId { get; set; }
    public ObjectId SourceEventId { get; set; }
}
