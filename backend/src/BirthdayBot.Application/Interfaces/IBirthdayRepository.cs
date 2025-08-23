using BirthdayBot.Domain.Entities;
using MongoDB.Bson;

namespace BirthdayBot.Application.Interfaces;

public interface IBirthdayRepository
{
    Task<Birthday> CreateAsync(Birthday birthday, CancellationToken ct = default);
    Task UpdateAsync(Birthday birthday, CancellationToken ct = default);
    Task DeleteAsync(ObjectId id, ObjectId userId, CancellationToken ct = default);
    Task<Birthday?> GetByIdAsync(ObjectId id, ObjectId userId, CancellationToken ct = default);
    Task<List<Birthday>> ListByUserAsync(ObjectId userId, CancellationToken ct = default);
    Task<Birthday?> FindByNameAsync(ObjectId userId, string name, CancellationToken ct = default);
}
