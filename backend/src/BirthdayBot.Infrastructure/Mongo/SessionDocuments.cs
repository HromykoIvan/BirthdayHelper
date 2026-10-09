using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BirthdayBot.Infrastructure.Mongo;

public sealed class ConversationSessionDocument
{
    [BsonId]
    public long ChatId { get; set; }

    public long UserId { get; set; }
    public AddWizardStep Step { get; set; }
    public string? Name { get; set; }
    public string? LastName { get; set; }
    public DateOnly? Date { get; set; }
    public bool? BirthYearKnown { get; set; }
    public string? TimeZoneId { get; set; }
    public string? Relation { get; set; }
    public string? Interests { get; set; }
    public string? Profession { get; set; }
    public string? Notes { get; set; }
    public Language? GreetingLanguage { get; set; }
    public bool WaitingCity { get; set; }
    public int? CalendarMessageId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public AddBirthdayWizardSession ToModel() => new(ChatId, UserId)
    {
        Step = Step,
        Name = Name,
        LastName = LastName,
        Date = Date,
        BirthYearKnown = BirthYearKnown,
        TimeZoneId = TimeZoneId,
        Relation = Relation,
        Interests = Interests,
        Profession = Profession,
        Notes = Notes,
        GreetingLanguage = GreetingLanguage,
        WaitingCity = WaitingCity,
        CalendarMessageId = CalendarMessageId
    };

    public static ConversationSessionDocument FromModel(
        AddBirthdayWizardSession session,
        DateTime expiresAtUtc) => new()
    {
        ChatId = session.ChatId,
        UserId = session.UserId,
        Step = session.Step,
        Name = session.Name,
        LastName = session.LastName,
        Date = session.Date,
        BirthYearKnown = session.BirthYearKnown,
        TimeZoneId = session.TimeZoneId,
        Relation = session.Relation,
        Interests = session.Interests,
        Profession = session.Profession,
        Notes = session.Notes,
        GreetingLanguage = session.GreetingLanguage,
        WaitingCity = session.WaitingCity,
        CalendarMessageId = session.CalendarMessageId,
        ExpiresAtUtc = expiresAtUtc
    };
}

public sealed class AiFeedbackSessionDocument
{
    [BsonId]
    public long ChatId { get; set; }

    public ObjectId SourceEventId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public AiFeedbackSession ToModel() => new()
    {
        ChatId = ChatId,
        SourceEventId = SourceEventId
    };
}
