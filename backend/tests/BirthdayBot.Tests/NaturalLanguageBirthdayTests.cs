using BirthdayBot.Application.Utils;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace BirthdayBot.Tests;

public class NaturalLanguageBirthdayTests
{
    [Theory]
    [InlineData("28 июля", 28, 7, false)]
    [InlineData("28 Июля", 28, 7, false)]
    [InlineData("28.07", 28, 7, false)]
    [InlineData("28.07.1989", 28, 7, true)]
    [InlineData("28 lipca", 28, 7, false)]
    [InlineData("28 July", 28, 7, false)]
    [InlineData("29 февраля", 29, 2, false)]
    public void DatesWithMonthNames_AreRecognized(string input, int day, int month, bool yearKnown)
    {
        BirthdayDateParser.TryParse(input, out var date, out var hasYear).Should().BeTrue();

        date.Day.Should().Be(day);
        date.Month.Should().Be(month);
        hasYear.Should().Be(yearKnown);
        if (!yearKnown)
        {
            date.Year.Should().Be(2000);
        }
    }

    [Theory]
    [InlineData("31 февраля")]
    [InlineData("32.07")]
    [InlineData("28 nonsense")]
    public void InvalidDates_AreRejected(string input)
    {
        BirthdayDateParser.TryParse(input, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("мамы", "mother")]
    [InlineData("маме", "mother")]
    [InlineData("мама", "mother")]
    [InlineData("mother", "mother")]
    [InlineData("żonie", "wife")]
    [InlineData("папы", "father")]
    public void RelationAliases_AreMapped(string text, string expected)
    {
        BirthdayRecipientMatcher.GetRelationshipKey(text).Should().Be(expected);
    }

    [Fact]
    public void FamilyCategory_DoesNotImply_SpecificMother()
    {
        var people = new[]
        {
            new Birthday { Name = "Татьяна", Relation = "Семья", Date = new DateOnly(2000, 7, 28) }
        };

        BirthdayRecipientMatcher.FindMatches(people, "мамы").Should().BeEmpty();
    }

    [Fact]
    public void SpecificMotherTag_CanBeResolved()
    {
        var mother = new Birthday { Name = "Татьяна", Relation = "Мама", Date = new DateOnly(2000, 7, 28) };
        var sister = new Birthday { Name = "Анна", Relation = "Сестра", Date = new DateOnly(2000, 4, 3) };

        BirthdayRecipientMatcher.FindMatches([mother, sister], "мамы").Should().ContainSingle().Which.Should().BeSameAs(mother);
    }

    [Fact]
    public void DuplicateNames_AreNotSilentlyCollapsed()
    {
        var people = new[]
        {
            new Birthday { Name = "Татьяна", LastName = "Громыко", Date = new DateOnly(1989, 1, 17) },
            new Birthday { Name = "Татьяна", LastName = "Громыко", Date = new DateOnly(2000, 7, 28) }
        };

        BirthdayRecipientMatcher.FindMatches(people, "Татьяна Громыко").Should().HaveCount(2);
    }
}
