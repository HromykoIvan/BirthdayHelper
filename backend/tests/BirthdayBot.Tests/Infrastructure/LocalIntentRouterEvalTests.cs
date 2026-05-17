using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Services;
using FluentAssertions;

namespace BirthdayBot.Tests.Infrastructure;

public class LocalIntentRouterEvalTests
{
    private static readonly User User = new()
    {
        Lang = Language.Ru,
        Tone = Tone.Friendly,
        Timezone = "Europe/Warsaw"
    };

    [Theory]
    [InlineData("удали Ивана", UserIntentType.RemoveByName)]
    [InlineData("delete alex", UserIntentType.RemoveByName)]
    [InlineData("покажи список", UserIntentType.OpenList)]
    [InlineData("open settings", UserIntentType.OpenSettings)]
    [InlineData("помощь", UserIntentType.OpenHelp)]
    [InlineData("добавить день рождения", UserIntentType.OpenAddBirthday)]
    [InlineData("сгенерируй поздравление для Сергей Калугин на 23 февраля", UserIntentType.GenerateGreetingPreview)]
    [InlineData("18:30", UserIntentType.UpdateSettings)]
    [InlineData("friendly", UserIntentType.UpdateSettings)]
    [InlineData("language en", UserIntentType.UpdateSettings)]
    public async Task Should_Parse_Real_Phrases(string text, UserIntentType expected)
    {
        var router = new LocalIntentRouter(new AiMetrics());

        var actual = await router.ParseAsync(User, text);

        actual.Intent.Should().Be(expected);
    }

    [Fact]
    public async Task Should_Not_Treat_English_Substring_As_Language_Command()
    {
        var router = new LocalIntentRouter(new AiMetrics());

        var actual = await router.ParseAsync(User, "my green friend");

        actual.Intent.Should().Be(UserIntentType.None);
    }
}
