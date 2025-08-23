// path: backend/src/BirthdayBot.Api/Options/MetricsOptions.cs
namespace BirthdayBot.Api.Options;

public class MetricsOptions
{
    public bool Enable { get; set; } = true;
    public string ScrapeEndpoint { get; set; } = "/metrics";
}