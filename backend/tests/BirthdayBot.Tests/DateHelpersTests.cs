using System;
using BirthdayBot.Domain.Utils;
using FluentAssertions;
using NodaTime;
using Xunit;

namespace BirthdayBot.Tests;

public class DateHelpersTests
{
    [Fact]
    public void NextBirthday_Today_Should_Return_Today_And_Age()
    {
        var dob = new DateOnly(1990, 8, 22);
        var today = new LocalDate(2025, 8, 22);
        var (next, age) = DateHelpers.NextBirthday(today, dob);
        next.Should().Be(new LocalDate(2025, 8, 22));
        age.Should().Be(35);
    }

    [Fact]
    public void NextBirthday_Passed_Should_Return_Next_Year()
    {
        var dob = new DateOnly(1990, 1, 10);
        var today = new LocalDate(2025, 8, 22);
        var (next, age) = DateHelpers.NextBirthday(today, dob);
        next.Year.Should().Be(2026);
        age.Should().Be(36);
    }
}