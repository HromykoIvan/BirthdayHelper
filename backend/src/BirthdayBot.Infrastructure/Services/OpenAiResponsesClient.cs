using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BirthdayBot.Infrastructure.Services;

public sealed record OpenAiCallResult<T>(
    T Value,
    string Model,
    int? InputTokens = null,
    int? OutputTokens = null);

/// <summary>
/// Minimal Responses API client. Keeps the application independent of a specific SDK version.
/// Sensitive values and user prompts are never logged.
/// </summary>
public sealed class OpenAiResponsesClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiResponsesClient> _logger;

    public OpenAiResponsesClient(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiResponsesClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<OpenAiCallResult<T>?> GenerateStructuredAsync<T>(
        string model,
        string instructions,
        string input,
        string schemaName,
        JsonObject schema,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var payload = new JsonObject
        {
            ["model"] = model,
            ["instructions"] = instructions,
            ["input"] = input,
            ["store"] = false,
            ["max_output_tokens"] = _options.MaxOutputTokens,
            ["reasoning"] = new JsonObject
            {
                ["effort"] = "none"
            },
            ["text"] = new JsonObject
            {
                ["verbosity"] = "low",
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = schemaName,
                    ["strict"] = true,
                    ["schema"] = schema.DeepClone()
                }
            }
        };

        request.Content = new StringContent(
            payload.ToJsonString(JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenAI Responses API returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var outputText = ExtractOutputText(root);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                _logger.LogWarning("OpenAI Responses API returned no output_text.");
                return null;
            }

            T? value;
            try
            {
                value = JsonSerializer.Deserialize<T>(outputText, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize structured OpenAI response.");
                return null;
            }

            if (value is null)
            {
                return null;
            }

            var inputTokens = TryGetInt(root, "usage", "input_tokens");
            var outputTokens = TryGetInt(root, "usage", "output_tokens");
            var actualModel = root.TryGetProperty("model", out var modelElement)
                ? modelElement.GetString() ?? model
                : model;

            return new OpenAiCallResult<T>(value, actualModel, inputTokens, outputTokens);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("OpenAI request timed out.");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OpenAI HTTP request failed.");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenAI response was not valid JSON.");
            return null;
        }
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "message", StringComparison.Ordinal))
            {
                continue;
            }

            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var partType) &&
                    string.Equals(partType.GetString(), "output_text", StringComparison.Ordinal) &&
                    part.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement root, string objectName, string propertyName)
    {
        if (root.TryGetProperty(objectName, out var obj) &&
            obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propertyName, out var value) &&
            value.TryGetInt32(out var result))
        {
            return result;
        }

        return null;
    }
}
