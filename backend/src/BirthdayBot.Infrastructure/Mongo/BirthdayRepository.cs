// path: backend/src/BirthdayBot.Infrastructure/Mongo/BirthdayRepository.cs
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BirthdayBot.Infrastructure.Mongo;

public sealed class BirthdayRepository : IBirthdayRepository
{
    private readonly MongoContext _ctx;

    public BirthdayRepository(MongoContext ctx) => _ctx = ctx;

    public async Task<ObjectId> CreateAsync(Birthday birthday, CancellationToken ct = default)
    {
        await _ctx.Birthdays.InsertOneAsync(birthday, cancellationToken: ct);
        return birthday.Id;
    }

    public async Task UpdateAsync(Birthday birthday, CancellationToken ct = default)
    {
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
        return await _ctx.Birthdays.Find(b => b.UserId == userId).SortBy(b => b.Date.Month).ThenBy(b => b.Date.Day).ToListAsync(ct);
    }

    public async Task<Birthday?> FindByNameAsync(ObjectId userId, string name, CancellationToken ct = default)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        // A user's birthday list is expected to be small. Loading the user's entries here
        // lets us match first name or full name reliably without locale-sensitive Mongo expressions.
        var entries = await _ctx.Birthdays.Find(b => b.UserId == userId).ToListAsync(ct);

        return entries.FirstOrDefault(b =>
                   string.Equals(b.FullName, normalized, StringComparison.OrdinalIgnoreCase))
               ?? entries.FirstOrDefault(b =>
                   string.Equals(b.Name, normalized, StringComparison.OrdinalIgnoreCase))
               ?? entries.FirstOrDefault(b =>
                   b.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
