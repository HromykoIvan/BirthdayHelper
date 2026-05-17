using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Infrastructure.Services;
using FluentAssertions;
using Moq;
using MongoDB.Bson;

namespace BirthdayBot.Tests.Infrastructure;

public class AiEvalServiceTests
{
    [Fact]
    public async Task BuildIntentSummaryAsync_Should_Calculate_Accuracy()
    {
        var repo = new Mock<IAiEventRepository>();
        repo.Setup(x => x.ListLabeledIntentEventsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AiEvent>
            {
                new() { ParsedIntent = "OpenHelp", ExpectedIntent = "OpenHelp" },
                new() { ParsedIntent = "OpenList", ExpectedIntent = "OpenSettings" },
                new() { ParsedIntent = "OpenList", ExpectedIntent = "OpenList" }
            });

        var service = new AiEvalService(repo.Object);
        var summary = await service.BuildIntentSummaryAsync();

        summary.TotalLabeled.Should().Be(3);
        summary.Correct.Should().Be(2);
        summary.Accuracy.Should().BeApproximately(0.666, 0.01);
        summary.PerIntent.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LabelIntentAsync_Should_Return_False_On_Invalid_Id()
    {
        var repo = new Mock<IAiEventRepository>();
        var service = new AiEvalService(repo.Object);

        var ok = await service.LabelIntentAsync("bad-id", "OpenHelp");

        ok.Should().BeFalse();
        repo.Verify(x => x.SetExpectedIntentAsync(It.IsAny<ObjectId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
