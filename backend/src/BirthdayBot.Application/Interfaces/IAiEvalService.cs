using BirthdayBot.Application.Models;

namespace BirthdayBot.Application.Interfaces;

public interface IAiEvalService
{
    Task<AiIntentEvalSummary> BuildIntentSummaryAsync(int take = 2000, CancellationToken ct = default);
    Task<List<AiIntentSample>> GetUnlabeledIntentSamplesAsync(int take = 50, CancellationToken ct = default);
    Task<bool> LabelIntentAsync(string eventId, string expectedIntent, CancellationToken ct = default);
}
