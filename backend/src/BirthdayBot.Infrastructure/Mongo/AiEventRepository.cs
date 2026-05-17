using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BirthdayBot.Infrastructure.Mongo;

public sealed class AiEventRepository : IAiEventRepository
{
    private readonly MongoContext _ctx;

    public AiEventRepository(MongoContext ctx) => _ctx = ctx;

    public async Task<AiEvent> CreateAsync(AiEvent aiEvent, CancellationToken ct = default)
    {
        await _ctx.AiEvents.InsertOneAsync(aiEvent, cancellationToken: ct);
        return aiEvent;
    }

    public async Task<AiEvent?> GetByIdAsync(ObjectId eventId, CancellationToken ct = default)
    {
        return await _ctx.AiEvents.Find(x => x.Id == eventId).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> MarkAcceptedExampleAsync(ObjectId eventId, bool accepted, CancellationToken ct = default)
    {
        var update = Builders<AiEvent>.Update.Set(x => x.IsAcceptedExample, accepted);
        var result = await _ctx.AiEvents.UpdateOneAsync(x => x.Id == eventId, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<List<AiEvent>> ListAcceptedGreetingExamplesAsync(ObjectId userId, int take = 3, CancellationToken ct = default)
    {
        var filter = Builders<AiEvent>.Filter.Eq(x => x.UserId, userId)
                     & Builders<AiEvent>.Filter.Eq(x => x.IsAcceptedExample, true)
                     & Builders<AiEvent>.Filter.Ne(x => x.OutputText, null)
                     & Builders<AiEvent>.Filter.In(x => x.EventType, new[] { "enhance", "enhance_reminder", "enhance_test", "enhance_regen", "enhance_comment_regen" });

        return await _ctx.AiEvents
            .Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Limit(Math.Clamp(take, 1, 20))
            .ToListAsync(ct);
    }

    public async Task<List<AiEvent>> ListRecentIntentEventsAsync(int take = 200, bool onlyUnlabeled = false, CancellationToken ct = default)
    {
        var filter = Builders<AiEvent>.Filter.Eq(x => x.EventType, "intent");
        if (onlyUnlabeled)
        {
            filter &= Builders<AiEvent>.Filter.Or(
                Builders<AiEvent>.Filter.Eq(x => x.ExpectedIntent, null),
                Builders<AiEvent>.Filter.Eq(x => x.ExpectedIntent, ""));
        }

        return await _ctx.AiEvents
            .Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Limit(Math.Clamp(take, 1, 5000))
            .ToListAsync(ct);
    }

    public async Task<List<AiEvent>> ListLabeledIntentEventsAsync(int take = 2000, CancellationToken ct = default)
    {
        var filter = Builders<AiEvent>.Filter.Eq(x => x.EventType, "intent")
                     & Builders<AiEvent>.Filter.Ne(x => x.ExpectedIntent, null)
                     & Builders<AiEvent>.Filter.Ne(x => x.ExpectedIntent, "");

        return await _ctx.AiEvents
            .Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Limit(Math.Clamp(take, 1, 10000))
            .ToListAsync(ct);
    }

    public async Task<bool> SetExpectedIntentAsync(ObjectId eventId, string expectedIntent, CancellationToken ct = default)
    {
        var update = Builders<AiEvent>.Update.Set(x => x.ExpectedIntent, expectedIntent);
        var result = await _ctx.AiEvents.UpdateOneAsync(x => x.Id == eventId, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }
}
