using BirthdayBot.Domain.Entities;
using BirthdayBot.Infrastructure.Services;
using FluentAssertions;

namespace BirthdayBot.Tests;

public sealed class MemoryFactParserTests
{
    [Theory]
    [InlineData("Аня любит походы", "Аня", "append_interests", "походы")]
    [InlineData("Мама обожает книги.", "Мама", "append_interests", "книги")]
    [InlineData("Маме понравилась идея подарить книгу", "Маме", "append_gifts", "книгу")]
    [InlineData("Маша хочет новый велосипед", "Маша", "append_gifts", "новый велосипед")]
    [InlineData("Anna loves hiking", "Anna", "append_interests", "hiking")]
    [InlineData("Anna wants a bicycle", "Anna", "append_gifts", "a bicycle")]
    [InlineData("Anna lubi podróże", "Anna", "append_interests", "podróże")]
    [InlineData("Mama nie lubi hałasu", "Mama", "memory", "nie lubi hałasu")]
    [InlineData("Аня не любит конфеты", "Аня", "memory", "не любит конфеты")]
    [InlineData("Mum dreams of a trip to Rome", "Mum", "memory", "dreams of a trip to Rome")]
    public void Detects_volunteered_fact_without_losing_its_meaning(
        string text, string subject, string field, string value)
    {
        MemoryFactParser.TryParse(text, out var fact).Should().BeTrue();
        fact.Should().NotBeNull();
        fact!.Subject.Should().Be(subject);
        fact.Field.Should().Be(field);
        fact.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("Аня любит походы?")]
    [InlineData("Аня любит")]
    [InlineData("Составь поздравление для Ани")]
    [InlineData("Добавь Аню 12.06")]
    [InlineData("/start")]
    [InlineData("Anna likes walking\nand books")]
    [InlineData("Что любит Аня?")]
    public void Does_not_capture_questions_commands_or_unrelated_messages(string text)
    {
        MemoryFactParser.TryParse(text, out var fact).Should().BeFalse();
        fact.Should().BeNull();
    }

    [Fact]
    public void Very_long_input_is_not_saved_as_a_person_fact()
    {
        MemoryFactParser.TryParse("Anna loves " + new string('x', 450), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Appending_a_fact_preserves_previous_interests_and_avoids_duplicates()
    {
        var person = new Birthday { Name = "Anna", Interests = "books" };
        PersonProfileEditor.TryApply(person, "append_interests", "hiking", out _).Should().BeTrue();
        PersonProfileEditor.TryApply(person, "append_interests", "HIKING", out _).Should().BeTrue();
        person.Interests.Should().Be("books; hiking");
    }

    [Fact]
    public void Appending_gift_does_not_replace_previous_gift_ideas()
    {
        var person = new Birthday { Name = "Anna", GiftIdeas = "Flowers" };
        PersonProfileEditor.TryApply(person, "append_gifts", "a new bike", out _).Should().BeTrue();
        person.GiftIdeas.Should().Be("Flowers; a new bike");
    }

    [Fact]
    public void An_append_cannot_clear_or_overfill_a_profile()
    {
        var person = new Birthday { Name = "Anna", Interests = new string('a', 1995) };
        PersonProfileEditor.TryApply(person, "append_interests", "new interest", out var error)
            .Should().BeFalse();
        error.Should().Be("notes_full");
        PersonProfileEditor.TryApply(person, "append_interests", "—", out _)
            .Should().BeFalse();
        person.Interests.Should().HaveLength(1995);
    }
}
