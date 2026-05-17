namespace BirthdayBot.Application.Models;

public sealed class AiIntentEvalSummary
{
    public int TotalLabeled { get; set; }

    public int Correct { get; set; }

    public double Accuracy => TotalLabeled == 0 ? 0 : (double)Correct / TotalLabeled;

    public List<AiIntentEvalIntentRow> PerIntent { get; set; } = new();
    public List<AiIntentEvalPromptVersionRow> PerPromptVersion { get; set; } = new();
}

public sealed class AiIntentEvalIntentRow
{
    public string Intent { get; set; } = string.Empty;

    public int Total { get; set; }

    public int Correct { get; set; }

    public double Accuracy => Total == 0 ? 0 : (double)Correct / Total;
}

public sealed class AiIntentEvalPromptVersionRow
{
    public string PromptVersion { get; set; } = "v1";
    public int Total { get; set; }
    public int Correct { get; set; }
    public double Accuracy => Total == 0 ? 0 : (double)Correct / Total;
}

public sealed class AiIntentSample
{
    public string EventId { get; set; } = string.Empty;
    public string InputText { get; set; } = string.Empty;
    public string ParsedIntent { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = "v1";
    public double? Confidence { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
