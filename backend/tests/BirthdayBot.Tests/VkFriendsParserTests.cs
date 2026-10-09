using BirthdayBot.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace BirthdayBot.Tests;

public class VkFriendsParserTests
{
    [Fact]
    public void ParsesVisibleFriendBirthdays_WithAndWithoutYear()
    {
        const string payload = """
            {
              "response": {
                "count": 4,
                "items": [
                  { "id": 111, "first_name": "Анна", "last_name": "Петрова", "bdate": "28.7.1989" },
                  { "id": 222, "first_name": "Иван", "last_name": "Иванов", "bdate": "3.4" },
                  { "id": 333, "first_name": "Без", "last_name": "Даты" },
                  { "id": 444, "first_name": "Неверный", "last_name": "День", "bdate": "31.2" }
                ]
              }
            }
            """;

        var result = VkFriendsParser.Parse(payload);

        result.TotalFriends.Should().Be(4);
        result.WithoutBirthday.Should().Be(1);
        result.Invalid.Should().Be(1);
        result.Entries.Should().HaveCount(2);
        result.Entries[0].FullName.Should().Be("Анна Петрова");
        result.Entries[0].Birthday.Should().Be(new DateOnly(1989, 7, 28));
        result.Entries[0].YearKnown.Should().BeTrue();

        result.Entries[1].Birthday.Should().Be(new DateOnly(2000, 4, 3));
        result.Entries[1].YearKnown.Should().BeFalse();
        result.Entries[1].FullName.Should().Be("Иван Иванов");
    }

    [Theory]
    [InlineData("29.2", 2000, 2, 29, false)]
    [InlineData("29.2.2000", 2000, 2, 29, true)]
    [InlineData("1.1.1998", 1998, 1, 1, true)]
    public void ParsesValidVkBirthdays(string input, int year, int month, int day, bool known)
    {
        VkFriendsParser.TryParseVkBirthday(input, out var date, out var hasYear).Should().BeTrue();

        date.Should().Be(new DateOnly(year, month, day));
        hasYear.Should().Be(known);
    }

    [Theory]
    [InlineData("29.2.2023")]
    [InlineData("31.4")]
    [InlineData("")]
    [InlineData("2033-02-02")]
    public void InvalidBirthdaysAreExcluded(string input)
    {
        VkFriendsParser.TryParseVkBirthday(input, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void ApiPermissionsErrorIsNotSilentlyTreatedAsEmptyFriends()
    {
        const string payload = """{"error":{"error_code":15,"error_msg":"Access denied"}}""";

        var action = () => VkFriendsParser.Parse(payload);

        action.Should().Throw<VkApiException>().Where(x => x.ApiErrorCode == 15);
    }

    [Fact]
    public void VkImporterDoesNotStoreOtherProfileFields()
    {
        const string payload = """
            {"response":{"count":1,"items":[{
              "id":917282,"first_name":"Маша","last_name":"Тест",
              "bdate":"1.10","photo_100":"https://vk.example/p.jpg",
              "mobile_phone":"+1111111","city":{"title":"Somewhere"},"about":"private details"
            }]}}
            """;

        var result = VkFriendsParser.Parse(payload);
        result.Entries.Should().ContainSingle();
        result.Entries[0].FullName.Should().Be("Маша Тест");
        result.Entries[0].ToString().Should().NotContain("1111111");
        result.Entries[0].ToString().Should().NotContain("private");
        result.Entries[0].ToString().Should().NotContain("vk.example");
    }
}
