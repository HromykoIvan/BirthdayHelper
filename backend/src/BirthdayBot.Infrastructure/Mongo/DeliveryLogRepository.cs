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

    public async Task<List<DeliveryLog>> ListForUserAsync(ObjectId userId, int take = 50, CancellationToken ct = default)
    {
        return await _ctx.DeliveryLogs.Find(l => l.UserId == userId).SortByDescending(l => l.WhenUtc).Limit(take).ToListAsync(ct);
    }
}
