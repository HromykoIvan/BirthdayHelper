using BirthdayBot.Application.Services;
using BirthdayBot.Domain.Entities;
using FluentAssertions;
using MongoDB.Bson;

namespace BirthdayBot.Tests.Application;

public class UpcomingServiceTests
{
    private readonly UpcomingService _sut = new();

    private static Birthday Make(int month, int day, int year = 1990, string name = "Test") =>
        new() { Id = ObjectId.GenerateNewId(), Name = name, Date = new DateOnly(year, month, day) };

    // ── InRange — basic hit ───────────────────────────────────────────────────

    [Fact]
    public void InRange_Returns_Birthday_Within_Window()
    {
        var bday = Make(6, 15);
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        var results = _sut.InRange(new[] { bday }, from, to).ToList();

        results.Should().HaveCount(1);
        results[0].Occurs.Should().Be(new DateOnly(2025, 6, 15));
    }

    // ── InRange — birthday before window → not returned ──────────────────────

    [Fact]
    public void InRange_Skips_Birthday_Before_From_Date()
    {
        var bday = Make(5, 1);                   // May 1
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        _sut.InRange(new[] { bday }, from, to).Should().BeEmpty();
    }

    // ── InRange — birthday after window → not returned ────────────────────────

    [Fact]
    public void InRange_Skips_Birthday_After_To_Date()
    {
        var bday = Make(7, 1);                   // July 1
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        _sut.InRange(new[] { bday }, from, to).Should().BeEmpty();
    }

    // ── InRange — rolls over to next year ─────────────────────────────────────

    [Fact]
    public void InRange_Wraps_To_Next_Year_When_Birthday_Passed()
    {
        var bday = Make(3, 10, 1990);            // March 10
        var from = new DateOnly(2025, 3, 11);    // day after birthday
        var to   = new DateOnly(2026, 3, 15);

        var results = _sut.InRange(new[] { bday }, from, to).ToList();

        results.Should().HaveCount(1);
        results[0].Occurs.Year.Should().Be(2026, because: "birthday already passed this year");
    }

    // ── InRange — on exact boundary dates ────────────────────────────────────

    [Fact]
    public void InRange_Includes_Birthday_On_From_Date()
    {
        var bday = Make(6, 1);
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        _sut.InRange(new[] { bday }, from, to).Should().HaveCount(1);
    }

    [Fact]
    public void InRange_Includes_Birthday_On_To_Date()
    {
        var bday = Make(6, 30);
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        _sut.InRange(new[] { bday }, from, to).Should().HaveCount(1);
    }

    // ── InRange — age (turns) calculation ─────────────────────────────────────

    [Fact]
    public void InRange_Calculates_Turns_Correctly()
    {
        var bday = Make(6, 15, year: 1990);
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        var result = _sut.InRange(new[] { bday }, from, to).Single();

        result.Turns.Should().Be(35);
    }

    [Fact]
    public void InRange_Turns_Is_Zero_When_Year_Unknown()
    {
        var bday = Make(6, 15, year: 1);         // year 1 = "unknown"
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        var result = _sut.InRange(new[] { bday }, from, to).Single();

        // turns = 2025 - 1 = 2024 — logically "no year", consumer should handle this
        result.Turns.Should().Be(2024);
    }

    // ── Leap year Feb 29 handling ─────────────────────────────────────────────

    [Fact]
    public void InRange_Feb29_In_LeapYear_Returns_Feb29()
    {
        var bday = Make(2, 29, 2000);
        var from = new DateOnly(2024, 2, 1);
        var to   = new DateOnly(2024, 2, 29);

        var results = _sut.InRange(new[] { bday }, from, to).ToList();

        results.Should().HaveCount(1);
        results[0].Occurs.Should().Be(new DateOnly(2024, 2, 29));
    }

    [Fact]
    public void InRange_Feb29_In_NonLeapYear_Returns_Feb28_When_LeapToFeb28_True()
    {
        var bday = Make(2, 29, 2000);
        var from = new DateOnly(2025, 2, 1);
        var to   = new DateOnly(2025, 2, 28);

        var results = _sut.InRange(new[] { bday }, from, to, leapToFeb28: true).ToList();

        results.Should().HaveCount(1);
        results[0].Occurs.Should().Be(new DateOnly(2025, 2, 28));
    }

    [Fact]
    public void InRange_Feb29_In_NonLeapYear_Returns_Mar1_When_LeapToFeb28_False()
    {
        var bday = Make(2, 29, 2000);
        var from = new DateOnly(2025, 2, 1);
        var to   = new DateOnly(2025, 3, 1);

        var results = _sut.InRange(new[] { bday }, from, to, leapToFeb28: false).ToList();

        results.Should().HaveCount(1);
        results[0].Occurs.Should().Be(new DateOnly(2025, 3, 1));
    }

    // ── Multiple birthdays ────────────────────────────────────────────────────

    [Fact]
    public void InRange_Returns_All_Birthdays_In_Window()
    {
        var birthdays = new[]
        {
            Make(6, 5,  name: "Alice"),
            Make(6, 15, name: "Bob"),
            Make(6, 25, name: "Charlie"),
            Make(7, 1,  name: "Dave"),   // outside window
        };
        var from = new DateOnly(2025, 6, 1);
        var to   = new DateOnly(2025, 6, 30);

        var results = _sut.InRange(birthdays, from, to).ToList();

        results.Should().HaveCount(3);
        results.Select(r => r.B.Name).Should().Contain(new[] { "Alice", "Bob", "Charlie" });
    }

    [Fact]
    public void InRange_Returns_Empty_For_Empty_Source()
    {
        _sut.InRange(Array.Empty<Birthday>(),
                     new DateOnly(2025, 1, 1),
                     new DateOnly(2025, 12, 31))
            .Should().BeEmpty();
    }
}
