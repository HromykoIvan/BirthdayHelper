// path: backend/tests/BirthdayBot.Tests/GreetingGeneratorTests.cs
using BirthdayBot.Domain.Enums;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace BirthdayBot.Tests;

public class GreetingGeneratorTests
{
    [Fact]
    public void Should_Generate_NonEmpty_Text_For_All_Langs_And_Tones()
    {
        var gen = new GreetingGenerator();
        foreach (var lang in new[] { Language.Ru, Language.Pl, Language.En })
        foreach (var tone in new[] { Tone.Formal, Tone.Friendly })
        {
            var text = gen.Generate(lang, tone, "Иван", 30);
            text.Should().NotBeNullOrWhiteSpace();
            text.Should().Contain("30");
        }
    }

    [Fact]
    public void Should_Generate_Personalized_Text_With_Relation_And_Interests()
    {
        var gen = new GreetingGenerator();
        var user = new User { Lang = Language.En, Tone = Tone.Friendly };
        var birthday = new Birthday
        {
            Name = "Alex",
            Relation = "friend",
            Interests = "chess"
        };

        var text = gen.GeneratePersonalized(user, birthday, 28);

        text.Should().Contain("Alex");
        text.Should().Contain("28");
        text.Should().Contain("friend");
        text.Should().Contain("chess");
    }
}