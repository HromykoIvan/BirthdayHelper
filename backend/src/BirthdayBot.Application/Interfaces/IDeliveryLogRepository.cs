using BirthdayBot.Domain.Entities;
using MongoDB.Bson;

namespace BirthdayBot.Application.Interfaces;

public interface IDeliveryLogRepository
{
    Task<DeliveryLog> CreateAsync(DeliveryLog log, CancellationToken ct = default);
    Task<bool> TryCreateAsync(DeliveryLog log, CancellationToken ct = default);
    Task UpdateStatusAsync(ObjectId id, string status, string? messageId = null, string? error = null, CancellationToken ct = default);
    Task DeleteAsync(ObjectId id, CancellationToken ct = default);
    Task<List<DeliveryLog>> ListForUserAsync(ObjectId userId, int take = 50, CancellationToken ct = default);
}
