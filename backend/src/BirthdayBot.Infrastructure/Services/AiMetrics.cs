using System.Diagnostics.Metrics;

namespace BirthdayBot.Infrastructure.Services;

public sealed class AiMetrics : IDisposable
{
    public const string MeterName = "BirthdayBot.AI";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _intentRequests;
    private readonly Counter<long> _intentFallbacks;
    private readonly Counter<long> _intentParseErrors;
    private readonly Histogram<double> _intentLatencyMs;
    private readonly Counter<long> _enhanceRequests;
    private readonly Counter<long> _enhanceFallbacks;
    private readonly Histogram<double> _enhanceLatencyMs;

    public AiMetrics()
    {
        _intentRequests = _meter.CreateCounter<long>("ai_intent_requests_total");
        _intentFallbacks = _meter.CreateCounter<long>("ai_intent_fallback_total");
        _intentParseErrors = _meter.CreateCounter<long>("ai_intent_parse_errors_total");
        _intentLatencyMs = _meter.CreateHistogram<double>("ai_intent_latency_ms");
        _enhanceRequests = _meter.CreateCounter<long>("ai_enhance_requests_total");
        _enhanceFallbacks = _meter.CreateCounter<long>("ai_enhance_fallback_total");
        _enhanceLatencyMs = _meter.CreateHistogram<double>("ai_enhance_latency_ms");
    }

    public void TrackIntentRequest(string source) =>
        _intentRequests.Add(1, KeyValuePair.Create<string, object?>("source", source));

    public void TrackIntentFallback(string reason) =>
        _intentFallbacks.Add(1, KeyValuePair.Create<string, object?>("reason", reason));

    public void TrackIntentParseError(string reason) =>
        _intentParseErrors.Add(1, KeyValuePair.Create<string, object?>("reason", reason));

    public void TrackIntentLatency(double elapsedMs, string source) =>
        _intentLatencyMs.Record(elapsedMs, KeyValuePair.Create<string, object?>("source", source));

    public void TrackEnhanceRequest(string source) =>
        _enhanceRequests.Add(1, KeyValuePair.Create<string, object?>("source", source));

    public void TrackEnhanceFallback(string reason) =>
        _enhanceFallbacks.Add(1, KeyValuePair.Create<string, object?>("reason", reason));

    public void TrackEnhanceLatency(double elapsedMs, string source) =>
        _enhanceLatencyMs.Record(elapsedMs, KeyValuePair.Create<string, object?>("source", source));

    public void Dispose() => _meter.Dispose();
}
