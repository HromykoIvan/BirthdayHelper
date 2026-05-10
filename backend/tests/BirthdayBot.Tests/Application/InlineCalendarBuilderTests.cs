using BirthdayBot.Application.UI;
using BirthdayBot.Domain.Enums;
using FluentAssertions;

namespace BirthdayBot.Tests.Application;

public class InlineCalendarBuilderTests
{
    // ── BuildMonthGrid shape ──────────────────────────────────────────────────

    [Theory]
    [InlineData(2025, 1)]   // January — 31 days, starts Wednesday
    [InlineData(2025, 2)]   // February — 28 days
    [InlineData(2024, 2)]   // February — 29 days (leap year)
    [InlineData(2025, 12)]  // December
    public void BuildMonthGrid_Contains_All_Days_Of_Month(int year, int month)
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(year, month, Language.En);
        var days = kb.InlineKeyboard
            .SelectMany(r => r)
            .Where(b => b.CallbackData!.StartsWith("cal:day:"))
            .Select(b => b.CallbackData!["cal:day:".Length..])
            .ToList();

        days.Should().HaveCount(DateTime.DaysInMonth(year, month));
    }

    [Fact]
    public void BuildMonthGrid_Day_Buttons_Have_Correct_Date_Format()
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(2025, 6, Language.En);
        var dayButtons = kb.InlineKeyboard
            .SelectMany(r => r)
            .Where(b => b.CallbackData!.StartsWith("cal:day:"))
            .ToList();

        foreach (var btn in dayButtons)
        {
            DateOnly.TryParse(btn.CallbackData!["cal:day:".Length..], out _)
                .Should().BeTrue($"'{btn.CallbackData}' should parse as a date");
        }
    }

    [Fact]
    public void BuildMonthGrid_Contains_Prev_And_Next_Navigation()
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(2025, 6, Language.En);
        var all = kb.InlineKeyboard.SelectMany(r => r).ToList();

        all.Should().Contain(b => b.CallbackData!.StartsWith("cal:prev:"),
            because: "previous month navigation is required");
        all.Should().Contain(b => b.CallbackData!.StartsWith("cal:next:"),
            because: "next month navigation is required");
    }

    [Fact]
    public void BuildMonthGrid_Contains_Manual_And_Cancel_Buttons()
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(2025, 6, Language.En);
        var all = kb.InlineKeyboard.SelectMany(r => r).ToList();

        all.Should().Contain(b => b.CallbackData == "cal:manual");
        all.Should().Contain(b => b.CallbackData == "cal:cancel");
    }

    // ── Month navigation wrap-around ──────────────────────────────────────────

    [Fact]
    public void BuildMonthGrid_January_Prev_Points_To_December_PreviousYear()
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(2025, 1, Language.En);
        var prevBtn = kb.InlineKeyboard.SelectMany(r => r)
            .First(b => b.CallbackData!.StartsWith("cal:prev:"));

        prevBtn.CallbackData.Should().Be("cal:prev:2024-12");
    }

    [Fact]
    public void BuildMonthGrid_December_Next_Points_To_January_NextYear()
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(2025, 12, Language.En);
        var nextBtn = kb.InlineKeyboard.SelectMany(r => r)
            .First(b => b.CallbackData!.StartsWith("cal:next:"));

        nextBtn.CallbackData.Should().Be("cal:next:2026-01");
    }

    // ── Header row has exactly 3 cells (prev / label / next) ─────────────────

    [Fact]
    public void BuildMonthGrid_Header_Row_Has_Three_Cells()
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(2025, 6, Language.En);
        kb.InlineKeyboard.First().Should().HaveCount(3);
    }

    // ── Day-of-week header row has 7 cells ────────────────────────────────────

    [Fact]
    public void BuildMonthGrid_DayOfWeek_Row_Has_Seven_Cells()
    {
        var kb = InlineCalendarBuilder.BuildMonthGrid(2025, 6, Language.En);
        kb.InlineKeyboard.Skip(1).First().Should().HaveCount(7);
    }

    // ── GetMonthName ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1,  "Январь")]
    [InlineData(6,  "Июнь")]
    [InlineData(12, "Декабрь")]
    public void GetMonthName_Returns_Russian_Month_Name(int month, string expected)
    {
        InlineCalendarBuilder.GetMonthName(month).Should().Be(expected);
    }
}
