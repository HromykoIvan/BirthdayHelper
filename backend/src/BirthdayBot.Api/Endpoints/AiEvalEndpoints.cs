using BirthdayBot.Application.Interfaces;

namespace BirthdayBot.Api.Endpoints;

public static class AiEvalEndpoints
{
    public static IEndpointRouteBuilder MapAiEvalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ai/eval/summary", async (int? take, IAiEvalService eval, CancellationToken ct) =>
        {
            var summary = await eval.BuildIntentSummaryAsync(take ?? 2000, ct);
            return Results.Ok(summary);
        })
        .WithName("GetAiEvalSummary")
        .WithTags("AI")
        .Produces(200);

        app.MapGet("/api/ai/eval/unlabeled", async (int? take, IAiEvalService eval, CancellationToken ct) =>
        {
            var rows = await eval.GetUnlabeledIntentSamplesAsync(take ?? 50, ct);
            return Results.Ok(rows);
        })
        .WithName("GetAiEvalUnlabeledSamples")
        .WithTags("AI")
        .Produces(200);

        app.MapPost("/api/ai/eval/label", async (AiIntentLabelRequest request, IAiEvalService eval, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.ExpectedIntent))
            {
                return Results.BadRequest(new { error = "eventId and expectedIntent are required" });
            }

            var updated = await eval.LabelIntentAsync(request.EventId, request.ExpectedIntent, ct);
            return updated ? Results.Ok(new { updated = true }) : Results.NotFound(new { updated = false });
        })
        .WithName("LabelAiIntent")
        .WithTags("AI")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        return app;
    }

    public sealed class AiIntentLabelRequest
    {
        public string EventId { get; set; } = string.Empty;
        public string ExpectedIntent { get; set; } = string.Empty;
    }
}
