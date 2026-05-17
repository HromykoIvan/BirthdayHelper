using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Application.Models;

public enum UserIntentType
{
    None = 0,
    OpenHelp = 1,
    OpenSettings = 2,
    OpenAddBirthday = 3,
    OpenList = 4,
    RemoveByName = 5,
    UpdateSettings = 6
}

public sealed record IntentParseResult(
    UserIntentType Intent,
    string? EntityName = null,
    SettingsUpdate? Settings = null,
    bool RequiresConfirmation = false,
    double Confidence = 0d)
{
    public static IntentParseResult NoMatch { get; } = new(UserIntentType.None);

    public static IntentParseResult ForNavigation(UserIntentType intent, double confidence = 0.9d) =>
        new(intent, Confidence: confidence);

    public static IntentParseResult ForRemove(string entityName, double confidence = 0.8d) =>
        new(UserIntentType.RemoveByName, EntityName: entityName, RequiresConfirmation: true, Confidence: confidence);

    public static IntentParseResult ForSettings(SettingsUpdate update, double confidence = 0.7d) =>
        new(UserIntentType.UpdateSettings, Settings: update, Confidence: confidence);
}
