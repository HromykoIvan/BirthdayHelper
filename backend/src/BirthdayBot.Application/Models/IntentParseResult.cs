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
    UpdateSettings = 6,
    GenerateGreetingPreview = 7,
    AddBirthdayFromText = 8,
    FindBirthday = 9
}

public sealed record IntentParseResult(
    UserIntentType Intent,
    string? EntityName = null,
    string? Occasion = null,
    SettingsUpdate? Settings = null,
    BirthdayDraft? Birthday = null,
    bool RequiresConfirmation = false,
    double Confidence = 0d,
    string ModelSource = "local-intent-router",
    int? InputTokens = null,
    int? OutputTokens = null)
{
    public static IntentParseResult NoMatch { get; } = new(UserIntentType.None);

    public static IntentParseResult ForNavigation(UserIntentType intent, double confidence = 0.9d) =>
        new(intent, Confidence: confidence);

    public static IntentParseResult ForRemove(string entityName, double confidence = 0.8d) =>
        new(UserIntentType.RemoveByName, EntityName: entityName, RequiresConfirmation: true, Confidence: confidence);

    public static IntentParseResult ForGreetingPreview(string entityName, string occasion, double confidence = 0.75d) =>
        new(UserIntentType.GenerateGreetingPreview, EntityName: entityName, Occasion: occasion, Confidence: confidence);

    public static IntentParseResult ForSettings(SettingsUpdate update, double confidence = 0.7d) =>
        new(UserIntentType.UpdateSettings, Settings: update, Confidence: confidence);

    public static IntentParseResult ForBirthdayDraft(BirthdayDraft draft, double confidence = 0.9d) =>
        new(UserIntentType.AddBirthdayFromText, EntityName: draft.FirstName, Birthday: draft,
            RequiresConfirmation: true, Confidence: confidence);

    public static IntentParseResult ForFindBirthday(string entityName, double confidence = 0.9d) =>
        new(UserIntentType.FindBirthday, EntityName: entityName, Confidence: confidence);
}
