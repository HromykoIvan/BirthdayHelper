using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Services;
using FluentAssertions;

namespace BirthdayBot.Tests.Infrastructure;

public class LocalizationServiceTests
{
    private readonly LocalizationService _sut = new();

    // ── Key exists for each language ─────────────────────────────────────────

    [Theory]
    [InlineData(Language.Ru, "cancel",  "❌ Отмена")]
    [InlineData(Language.Pl, "cancel",  "❌ Anuluj")]
    [InlineData(Language.En, "cancel",  "❌ Cancel")]
    [InlineData(Language.Ru, "skip",    "➡️ Пропустить")]
    [InlineData(Language.Pl, "skip",    "➡️ Pomiń")]
    [InlineData(Language.En, "skip",    "➡️ Skip")]
    public void GetText_Returns_Correct_Text_For_Language(Language lang, string key, string expected)
    {
        _sut.GetText(lang, key).Should().Be(expected);
    }

    // ── All three languages have the same set of keys ─────────────────────────

    [Theory]
    [InlineData("menu_add")]
    [InlineData("menu_list")]
    [InlineData("menu_settings")]
    [InlineData("menu_help")]
    [InlineData("wizard_start")]
    [InlineData("wizard_date_prompt")]
    [InlineData("wizard_saved_birthday")]
    [InlineData("wizard_cancelled")]
    [InlineData("ask_relation")]
    [InlineData("ask_interests")]
    [InlineData("confirm_summary")]
    [InlineData("relation_family")]
    [InlineData("relation_friend")]
    [InlineData("today")]
    [InlineData("tomorrow")]
    [InlineData("skip")]
    [InlineData("cancel")]
    [InlineData("error_save_failed")]
    [InlineData("error_try_again")]
    public void GetText_KeyExists_For_All_Languages(string key)
    {
        foreach (var lang in new[] { Language.Ru, Language.Pl, Language.En })
        {
            var result = _sut.GetText(lang, key);
            result.Should().NotBeNullOrEmpty(because: $"key '{key}' should be defined for {lang}");
            // The service returns the key itself when not found — verify it didn't fall back
            result.Should().NotBe(key, because: $"key '{key}' should be translated for {lang}");
        }
    }

    // ── Fallback to English ───────────────────────────────────────────────────

    [Fact]
    public void GetText_Unknown_Language_Uses_English_Fallback()
    {
        // Simulate an unhandled language cast value
        var unknownLang = (Language)999;
        var result = _sut.GetText(unknownLang, "cancel");
        // Falls back to English
        result.Should().Be("❌ Cancel");
    }

    // ── Missing key returns the key itself ────────────────────────────────────

    [Fact]
    public void GetText_MissingKey_Returns_Key_As_Fallback()
    {
        var result = _sut.GetText(Language.En, "totally_nonexistent_key_xyz");
        result.Should().Be("totally_nonexistent_key_xyz");
    }

}
