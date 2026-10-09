using BirthdayBot.Domain.Entities;
using BirthdayBot.Infrastructure.Services;
using FluentAssertions;

namespace BirthdayBot.Tests;

public class PersonProfileEditorTests
{
    [Fact]
    public void Name_can_be_updated_without_changing_other_details()
    {
        var person = new Birthday
        {
            Name = "Anna", LastName = "Kowalska", Date = new DateOnly(2000, 6, 12),
            BirthYearKnown = false, Interests = "running"
        };
        PersonProfileEditor.TryApply(person, "name", "Maria Nowak", out var error).Should().BeTrue();
        error.Should().BeEmpty();
        person.FullName.Should().Be("Maria Nowak");
        person.Interests.Should().Be("running");
        person.HasKnownBirthYear.Should().BeFalse();
    }

    [Fact]
    public void Birthday_without_year_remains_unknown()
    {
        var person = new Birthday { Name = "Anna", Date = new DateOnly(1990, 6, 12) };
        PersonProfileEditor.TryApply(person, "date", "29.02", out _).Should().BeTrue();
        person.Date.Should().Be(new DateOnly(2000, 2, 29));
        person.HasKnownBirthYear.Should().BeFalse();
    }

    [Fact]
    public void Invalid_date_does_not_overwrite_existing_date()
    {
        var person = new Birthday { Name = "Anna", Date = new DateOnly(1990, 6, 12) };
        PersonProfileEditor.TryApply(person, "date", "31.02", out var error).Should().BeFalse();
        error.Should().Be("invalid_date");
        person.Date.Should().Be(new DateOnly(1990, 6, 12));
    }

    [Fact]
    public void New_memories_append_instead_of_erasing_previous_notes()
    {
        var person = new Birthday { Name = "Anna", Notes = "Likes tea" };
        PersonProfileEditor.TryApply(person, "memory", "Dreams about Italy", out _).Should().BeTrue();
        person.Notes.Should().Be("Likes tea\n• Dreams about Italy");
    }

    [Fact]
    public void Clearing_an_optional_field_does_not_remove_birthday()
    {
        var person = new Birthday { Name = "Anna", Date = new DateOnly(2000, 6, 12), GiftIdeas = "Books" };
        PersonProfileEditor.TryApply(person, "gifts", "—", out _).Should().BeTrue();
        person.GiftIdeas.Should().BeNull();
        PersonProfileEditor.TryApply(person, "date", "—", out _).Should().BeFalse();
        person.Date.Should().Be(new DateOnly(2000, 6, 12));
    }

    [Fact]
    public void Too_long_memory_is_rejected_without_mutation()
    {
        var person = new Birthday { Name = "Anna", Notes = "original" };
        PersonProfileEditor.TryApply(person, "memory", new string('a', 501), out var error).Should().BeFalse();
        error.Should().Be("too_long");
        person.Notes.Should().Be("original");
    }

    [Fact]
    public void Unknown_field_cannot_modify_profile()
    {
        var person = new Birthday { Name = "Anna" };
        PersonProfileEditor.TryApply(person, "delete", "yes", out var error).Should().BeFalse();
        error.Should().Be("unknown");
        person.Name.Should().Be("Anna");
    }
}
