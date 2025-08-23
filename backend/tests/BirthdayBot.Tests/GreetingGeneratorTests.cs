// path: backend/tests/BirthdayBot.Tests/GreetingGeneratorTests.cs
using BirthdayBot.Domain.Enums;
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
}