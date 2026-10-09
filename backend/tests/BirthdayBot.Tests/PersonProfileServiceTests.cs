using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Mongo;
using BirthdayBot.Infrastructure.Options;
using BirthdayBot.Infrastructure.Services;
using BirthdayBot.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using Telegram.Bot.Types;

namespace BirthdayBot.Tests;

public sealed class PersonProfileServiceTests
{
    private readonly FakeTelegramBotClient _bot = new();
    private readonly Mock<IBirthdayRepository> _repo = new();

    private PersonProfileService CreateService()
    {
        // MongoClient and IMongoCollection construction do not connect to MongoDB.
        // These rendering tests intentionally require no live database.
        var context = new MongoContext(
            Options.Create(new MongoOptions
            {
                ConnectionString = "mongodb://127.0.0.1:27017",
                Database = "birthdaybot_unit_tests"
            }),
            NullLogger<MongoContext>.Instance);

        return new PersonProfileService(_bot, _repo.Object, context);
    }

    [Fact]
    public async Task Empty_directory_offers_an_easy_way_to_add_someone()
    {
        var user = new BirthdayBot.Domain.Entities.User { Lang = Language.Ru };
        _repo.Setup(x => x.ListByUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Birthday>());

        await CreateService().ShowPeopleAsync(user, 1001, 0, null, CancellationToken.None);

        _bot.SentRequests.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Directory_renders_existing_people_without_migrating_documents()
    {
        var user = new BirthdayBot.Domain.Entities.User { Lang = Language.En };
        var people = new List<Birthday>
        {
            new() { Id = ObjectId.GenerateNewId(), Name = "Anna", Date = new DateOnly(2000, 2, 29), BirthYearKnown = false },
            new() { Id = ObjectId.GenerateNewId(), Name = "Bob", Date = new DateOnly(1987, 7, 12) }
        };
        _repo.Setup(x => x.ListByUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(people);

        await CreateService().ShowPeopleAsync(user, 1001, 0, null, CancellationToken.None);

        _bot.SentRequests.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Opening_profile_only_fetches_contact_owned_by_current_user()
    {
        var user = new BirthdayBot.Domain.Entities.User { Lang = Language.Ru, Id = ObjectId.GenerateNewId() };
        var person = new Birthday
        {
            Id = ObjectId.GenerateNewId(), UserId = user.Id,
            Name = "Anna", Date = new DateOnly(2000, 2, 29),
            BirthYearKnown = false
        };
        _repo.Setup(x => x.GetByIdAsync(person.Id, user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(person);
        var callback = new CallbackQuery
        {
            Id = "callback-test",
            Data = $"person:show:{person.Id}",
            From = new Telegram.Bot.Types.User { Id = 2002, FirstName = "Test" },
            Message = new Message { Chat = new Chat { Id = 1001 }, MessageId = 42 }
        };

        await CreateService().HandleCallbackAsync(user, callback, CancellationToken.None);

        _repo.Verify(x => x.GetByIdAsync(person.Id, user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _bot.SentRequests.Should().NotBeEmpty();
    }
}
