using BirthdayBot.Application.Utils;
using FluentAssertions;

namespace BirthdayBot.Tests.Application;

public class FormattingTests
{
    // ── Html escaping ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("A & B", "A &amp; B")]
    [InlineData("<script>", "&lt;script&gt;")]
    [InlineData("a < b > c", "a &lt; b &gt; c")]
    [InlineData("", "")]
    [InlineData("No special chars 123", "No special chars 123")]
    public void Html_Escapes_SpecialCharacters(string input, string expected)
    {
        Formatting.Html(input).Should().Be(expected);
    }

    [Fact]
    public void Html_Escapes_Combined_Characters()
    {
        Formatting.Html("<a href=\"x\">A & B</a>")
            .Should().Be("&lt;a href=\"x\"&gt;A &amp; B&lt;/a&gt;");
    }

    // ── PluralYears ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1,   "год")]
    [InlineData(21,  "год")]
    [InlineData(101, "год")]
    [InlineData(2,   "года")]
    [InlineData(3,   "года")]
    [InlineData(4,   "года")]
    [InlineData(22,  "года")]
    [InlineData(5,   "лет")]
    [InlineData(10,  "лет")]
    [InlineData(11,  "лет")]   // exception: 11 → лет (not год)
    [InlineData(12,  "лет")]   // 12 → лет (not года)
    [InlineData(13,  "лет")]
    [InlineData(14,  "лет")]
    [InlineData(100, "лет")]
    [InlineData(111, "лет")]
    [InlineData(0,   "лет")]
    public void PluralYears_Returns_Correct_Form(int n, string expected)
    {
        Formatting.PluralYears(n).Should().Be(expected);
    }

    // ── BirthdayCard ─────────────────────────────────────────────────────────

    [Fact]
    public void BirthdayCard_Contains_Name_And_Age()
    {
        var birth  = new DateOnly(1990, 6, 15);
        var occurs = new DateOnly(2025, 6, 15);
        var card = Formatting.BirthdayCard("Alice", birth, occurs, 35);

        card.Should().Contain("Alice");
        card.Should().Contain("35");
        card.Should().Contain("1990-06-15");
    }

    [Fact]
    public void BirthdayCard_Escapes_Html_In_Name()
    {
        var birth  = new DateOnly(2000, 1, 1);
        var occurs = new DateOnly(2025, 1, 1);
        var card = Formatting.BirthdayCard("A<B>C", birth, occurs, 25);

        card.Should().Contain("A&lt;B&gt;C");
        card.Should().NotContain("<B>");
    }
}
