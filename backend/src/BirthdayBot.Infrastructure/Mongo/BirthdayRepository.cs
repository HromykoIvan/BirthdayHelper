// path: backend/src/BirthdayBot.Infrastructure/Mongo/BirthdayRepository.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BirthdayBot.Infrastructure.Mongo;

public class BirthdayRepository : IBirthdayRepository
{
    private readonly MongoContext _ctx;
    public BirthdayRepository(MongoContext ctx) => _ctx = ctx;

    public async Task<Birthday> CreateAsync(Birthday birthday, CancellationToken ct = default)
    {
        await _ctx.Birthdays.InsertOneAsync(birthday, cancellationToken: ct);
        return birthday;
    }

    public async Task UpdateAsync(Birthday birthday, CancellationToken ct = default)
    {
        birthday.UpdatedAt = DateTime.UtcNow;
        await _ctx.Birthdays.ReplaceOneAsync(b => b.Id == birthday.Id && b.UserId == birthday.UserId, birthday, cancellationToken: ct);
    }

    public async Task DeleteAsync(ObjectId id, ObjectId userId, CancellationToken ct = default)
    {
        await _ctx.Birthdays.DeleteOneAsync(b => b.Id == id && b.UserId == userId, ct);
    }

    public async Task<Birthday?> GetByIdAsync(ObjectId id, ObjectId userId, CancellationToken ct = default)
    {
        return await _ctx.Birthdays.Find(b => b.Id == id && b.UserId == userId).FirstOrDefaultAsync(ct);
    }

    public async Task<List<Birthday>> ListByUserAsync(ObjectId userId, CancellationToken ct = default)
    {
        return await _ctx.Birthdays.Find(b => b.UserId == userId).SortBy(b => b.DateOfBirth.Month).ThenBy(b => b.DateOfBirth.Day).ToListAsync(ct);
    }

    public async Task<Birthday?> FindByNameAsync(ObjectId userId, string name, CancellationToken ct = default)
    {
        return await _ctx.Birthdays.Find(b => b.UserId == userId && b.Name.ToLower() == name.ToLower()).FirstOrDefaultAsync(ct);
    }
}
