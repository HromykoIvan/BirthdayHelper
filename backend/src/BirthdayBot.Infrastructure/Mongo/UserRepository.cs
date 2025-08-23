// path: backend/src/BirthdayBot.Infrastructure/Mongo/UserRepository.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BirthdayBot.Infrastructure.Mongo;

public class UserRepository : IUserRepository
{
    private readonly MongoContext _ctx;
    public UserRepository(MongoContext ctx) => _ctx = ctx;

    public async Task<User?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        return await _ctx.Users.Find(x => x.TelegramUserId == telegramUserId).FirstOrDefaultAsync(ct);
    }

    public async Task<User> UpsertAsync(User user, CancellationToken ct = default)
    {
        var filter = Builders<User>.Filter.Eq(u => u.TelegramUserId, user.TelegramUserId);
        var options = new FindOneAndReplaceOptions<User> { IsUpsert = true, ReturnDocument = ReturnDocument.After };
        return await _ctx.Users.FindOneAndReplaceAsync(filter, user, options, ct);
    }

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        await _ctx.Users.InsertOneAsync(user, cancellationToken: ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        await _ctx.Users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: ct);
    }

    public async Task<User?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
    {
        return await _ctx.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
    }
}
