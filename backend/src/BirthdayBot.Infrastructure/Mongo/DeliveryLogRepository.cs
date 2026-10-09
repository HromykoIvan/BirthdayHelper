// path: backend/src/BirthdayBot.Infrastructure/Mongo/DeliveryLogRepository.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BirthdayBot.Infrastructure.Mongo;

public class DeliveryLogRepository : IDeliveryLogRepository
{
    private readonly MongoContext _ctx;
    public DeliveryLogRepository(MongoContext ctx) => _ctx = ctx;

    public async Task<DeliveryLog> CreateAsync(DeliveryLog log, CancellationToken ct = default)
    {
        await _ctx.DeliveryLogs.InsertOneAsync(log, cancellationToken: ct);
        return log;
    }

    public async Task<bool> TryCreateAsync(DeliveryLog log, CancellationToken ct = default)
    {
        try
        {
            await _ctx.DeliveryLogs.InsertOneAsync(log, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task UpdateStatusAsync(
        ObjectId id,
        string status,
        string? messageId = null,
        string? error = null,
        CancellationToken ct = default)
    {
        var update = Builders<DeliveryLog>.Update
            .Set(x => x.Status, status)
            .Set(x => x.MessageId, messageId)
            .Set(x => x.Error, error);

        await _ctx.DeliveryLogs.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
    }

    public Task DeleteAsync(ObjectId id, CancellationToken ct = default) =>
        _ctx.DeliveryLogs.DeleteOneAsync(x => x.Id == id, ct);

    public async Task<List<DeliveryLog>> ListForUserAsync(ObjectId userId, int take = 50, CancellationToken ct = default)
    {
        return await _ctx.DeliveryLogs.Find(l => l.UserId == userId).SortByDescending(l => l.WhenUtc).Limit(take).ToListAsync(ct);
    }
}
