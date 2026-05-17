using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using MongoDB.Bson;

namespace BirthdayBot.Infrastructure.Services;

public sealed class AiEvalService : IAiEvalService
{
    private readonly IAiEventRepository _events;

    public AiEvalService(IAiEventRepository events) => _events = events;

    public async Task<AiIntentEvalSummary> BuildIntentSummaryAsync(int take = 2000, CancellationToken ct = default)
    {
        var rows = await _events.ListLabeledIntentEventsAsync(take, ct);
        var summary = new AiIntentEvalSummary
        {
            TotalLabeled = rows.Count,
            Correct = rows.Count(x => Normalize(x.ParsedIntent) == Normalize(x.ExpectedIntent))
        };

        var grouped = rows
            .GroupBy(x => Normalize(x.ExpectedIntent) ?? "unknown")
            .OrderByDescending(g => g.Count());

        foreach (var g in grouped)
        {
            summary.PerIntent.Add(new AiIntentEvalIntentRow
            {
                Intent = g.Key,
                Total = g.Count(),
                Correct = g.Count(x => Normalize(x.ParsedIntent) == Normalize(x.ExpectedIntent))
            });
        }

        var byPrompt = rows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.PromptVersion) ? "unknown" : x.PromptVersion)
            .OrderByDescending(g => g.Count());

        foreach (var g in byPrompt)
        {
            summary.PerPromptVersion.Add(new AiIntentEvalPromptVersionRow
            {
                PromptVersion = g.Key,
                Total = g.Count(),
                Correct = g.Count(x => Normalize(x.ParsedIntent) == Normalize(x.ExpectedIntent))
            });
        }

        return summary;
    }

    public async Task<List<AiIntentSample>> GetUnlabeledIntentSamplesAsync(int take = 50, CancellationToken ct = default)
    {
        var rows = await _events.ListRecentIntentEventsAsync(take, onlyUnlabeled: true, ct);
        return rows.Select(x => new AiIntentSample
        {
            EventId = x.Id.ToString(),
            InputText = x.InputText ?? string.Empty,
            ParsedIntent = x.ParsedIntent ?? "none",
            PromptVersion = x.PromptVersion,
            Confidence = x.Confidence,
            CreatedAtUtc = x.CreatedAtUtc
        }).ToList();
    }

    public async Task<bool> LabelIntentAsync(string eventId, string expectedIntent, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(eventId, out var oid))
        {
            return false;
        }

        return await _events.SetExpectedIntentAsync(oid, expectedIntent.Trim(), ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
