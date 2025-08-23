using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Application.Models;

public record SettingsUpdate(string? TimeHHmm = null, string? Timezone = null, Language? Lang = null, bool? AutoGenerate = null, Tone? Tone = null);