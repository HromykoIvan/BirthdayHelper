using BirthdayBot.Application.Services;
using FluentAssertions;
using Xunit;

namespace BirthdayBot.Tests;

public class ContactImportParserTests
{
    [Fact]
    public void VCard_ImportsOnlyNamesAndBirthdays_AndSkipsContactsWithoutBirthday()
    {
        var vcf = """
            BEGIN:VCARD
            VERSION:3.0
            N:Громыко;Татьяна;;;
            FN:Татьяна Громыко
            TEL;TYPE=CELL:+48123456789
            EMAIL:t@example.org
            ADR:;;Private Street;Warsaw;;;
            BDAY:1989-01-17
            END:VCARD
            BEGIN:VCARD
            VERSION:3.0
            FN:Alex Smith
            BDAY:--07-28
            END:VCARD
            BEGIN:VCARD
            VERSION:3.0
            FN:Someone Without Birthday
            TEL:1234
            END:VCARD
            """;

        var result = ContactImportParser.Parse("contacts.vcf", vcf);

        result.Entries.Should().HaveCount(2);
        result.WithoutBirthday.Should().Be(1);
        result.Entries[0].FirstName.Should().Be("Татьяна");
        result.Entries[0].LastName.Should().Be("Громыко");
        result.Entries[0].Birthday.Should().Be(new DateOnly(1989, 1, 17));
        result.Entries[0].YearKnown.Should().BeTrue();
        result.Entries[1].Birthday.Should().Be(new DateOnly(2000, 7, 28));
        result.Entries[1].YearKnown.Should().BeFalse();
        result.Entries.Should().OnlyContain(x =>
            !x.FullName.Contains("1234") &&
            !x.FullName.Contains("@"));
    }

    [Fact]
    public void VCard_UnfoldedLines_AndCompactBirthday_AreHandled()
    {
        var vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Alex\r\n Smith\r\nBDAY:19970512\r\nEND:VCARD\r\n";

        var result = ContactImportParser.Parse("contacts.VCF", vcf);

        result.Entries.Should().ContainSingle();
        result.Entries[0].FullName.Should().Be("AlexSmith");
        result.Entries[0].Birthday.Should().Be(new DateOnly(1997, 5, 12));
    }

    [Fact]
    public void GoogleCsv_ImportsBirthdayColumns_AndQuotedFields()
    {
        const string csv = """
            Name,Given Name,Family Name,Birthday 1 - Value,Phone 1 - Value,Notes
            "Smith, Alex",Alex,Smith,1990-04-21,+1234,"Secret, private"
            "Татьяна Громыко",Татьяна,Громыко,--07-28,+5678,"Sensitive note"
            "No Date",No,Date,,,
            """;

        var result = ContactImportParser.Parse("google.csv", csv);

        result.Entries.Should().HaveCount(2);
        result.WithoutBirthday.Should().Be(1);
        result.Entries[0].FullName.Should().Be("Alex Smith");
        result.Entries[1].FullName.Should().Be("Татьяна Громыко");
        result.Entries[1].YearKnown.Should().BeFalse();
    }

    [Theory]
    [InlineData("--02-29", 2000, 2, 29, false)]
    [InlineData("--0229", 2000, 2, 29, false)]
    [InlineData("1980-01-09", 1980, 1, 9, true)]
    [InlineData("19800109", 1980, 1, 9, true)]
    [InlineData("28 июля", 2000, 7, 28, false)]
    [InlineData("28 lipca", 2000, 7, 28, false)]
    public void Birthdays_FromDifferentExports_AreParsed(
        string input, int year, int month, int day, bool known)
    {
        ContactImportParser.TryBirthday(input, out var date, out var yearKnown).Should().BeTrue();

        date.Should().Be(new DateOnly(year, month, day));
        yearKnown.Should().Be(known);
    }

    [Theory]
    [InlineData("--02-30")]
    [InlineData("not a birthday")]
    [InlineData("2023-02-29")]
    public void InvalidBirthday_IsRejected(string input)
    {
        ContactImportParser.TryBirthday(input, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void CsvWithNoRecognizedBirthdayHeader_IsRejected()
    {
        var act = () => ContactImportParser.Parse("x.csv", "Name,Email\nAlex,test@x.com");

        act.Should().Throw<ArgumentException>();
    }
}
