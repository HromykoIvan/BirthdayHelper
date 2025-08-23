using BirthdayBot.Domain.Entities;
using MongoDB.Bson;

namespace BirthdayBot.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct = default);
    Task<User> UpsertAsync(User user, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task<User?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
}
