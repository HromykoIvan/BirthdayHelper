using BirthdayBot.Domain.Entities;
using MongoDB.Bson;

namespace BirthdayBot.Application.Interfaces;

public interface IAiEventRepository
{
    Task<AiEvent> CreateAsync(AiEvent aiEvent, CancellationToken ct = default);
    Task<AiEvent?> GetByIdAsync(ObjectId eventId, CancellationToken ct = default);
    Task<bool> MarkAcceptedExampleAsync(ObjectId eventId, bool accepted, CancellationToken ct = default);
    Task<List<AiEvent>> ListAcceptedGreetingExamplesAsync(ObjectId userId, int take = 3, CancellationToken ct = default);
    Task<List<AiEvent>> ListRecentIntentEventsAsync(int take = 200, bool onlyUnlabeled = false, CancellationToken ct = default);
    Task<List<AiEvent>> ListLabeledIntentEventsAsync(int take = 2000, CancellationToken ct = default);
    Task<bool> SetExpectedIntentAsync(ObjectId eventId, string expectedIntent, CancellationToken ct = default);
}
