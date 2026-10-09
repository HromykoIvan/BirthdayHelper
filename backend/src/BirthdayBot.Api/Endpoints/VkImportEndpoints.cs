using System.Text.Json;
using BirthdayBot.Infrastructure.Services;

namespace BirthdayBot.Api.Endpoints;

public static class VkImportEndpoints
{
    public static IEndpointRouteBuilder MapVkImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/integrations/vk/callback", new[] { "GET", "POST" }, async (
            HttpRequest request,
            VkImportService importer,
            CancellationToken ct) =>
        {
            if (!importer.IsEnabled)
                return Results.NotFound();

            // VK ID Redirect mode may return flat query/form params or a payload JSON object.
            // Never render query params (authorization codes) into the response.
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in request.Query)
                values[pair.Key] = pair.Value.ToString();

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                foreach (var pair in form)
                    values[pair.Key] = pair.Value.ToString();
            }

            if (values.TryGetValue("payload", out var payload) && payload.Length <= 4096)
            {
                try
                {
                    using var parsed = JsonDocument.Parse(payload);
                    foreach (var key in new[] { "code", "state", "device_id", "error" })
                        if (parsed.RootElement.TryGetProperty(key, out var value) &&
                            value.ValueKind == JsonValueKind.String)
                            values[key] = value.GetString() ?? "";
                }
                catch (JsonException)
                {
                    return Results.BadRequest("Invalid VK ID callback.");
                }
            }

            values.TryGetValue("code", out var code);
            values.TryGetValue("state", out var state);
            values.TryGetValue("device_id", out var deviceId);

            if (string.IsNullOrWhiteSpace(state) || state.Length > 256 ||
                (code?.Length ?? 0) > 2048 || (deviceId?.Length ?? 0) > 256)
                return Results.BadRequest("Invalid VK ID authorization response.");

            var success = await importer.CompleteAsync(code, state, deviceId, ct);
            return Results.Content(
                success
                    ? "VK import preview sent to Telegram. Return to the Birthday Reminder bot."
                    : "VK import could not be completed. Return to Telegram and try again.",
                "text/plain; charset=utf-8");
        }).ExcludeFromDescription();

        return app;
    }
}
