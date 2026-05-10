using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;
using BirthdayBot.Application.Services;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Infrastructure.Services;
using BirthdayBot.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Telegram.Bot.Types;

namespace BirthdayBot.Tests.Application;

/// <summary>
/// Tests for the AddBirthdayWizardFlow covering step transitions,
/// calendar callbacks, save, cancel, and validation paths.
/// </summary>
public class AddBirthdayWizardFlowTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private readonly FakeTelegramBotClient              _fakeBot   = new();
    private readonly Mock<IConversationSessionStore>    _storeMock  = new(MockBehavior.Loose);
    private readonly Mock<IBirthdayRepository>          _repoMock   = new(MockBehavior.Loose);
    private readonly Mock<IUserRepository>              _usersMock  = new(MockBehavior.Loose);
    private readonly LocalizationService                _i18n       = new();

    private const long ChatId = 1001L;
    private const long UserId = 2002L;

    private AddBirthdayWizardFlow CreateFlow() =>
        new(_fakeBot, _storeMock.Object, _repoMock.Object,
            _usersMock.Object, _i18n, NullLogger<AddBirthdayWizardFlow>.Instance);

    private static Update TextUpdate(string text) => new()
    {
        Message = new Message
        {
            Chat = new Chat { Id = ChatId },
            From = new Telegram.Bot.Types.User { Id = UserId, FirstName = "Test" },
            Text = text
        }
    };

    private static Update CallbackUpdate(string data, int msgId = 1) => new()
    {
        CallbackQuery = new CallbackQuery
        {
            Id   = "cq1",
            Data = data,
            From = new Telegram.Bot.Types.User { Id = UserId, FirstName = "Test" },
            Message = new Message
            {
                MessageId = msgId,
                Chat      = new Chat { Id = ChatId }
            }
        }
    };

    private void SetupSession(AddBirthdayWizardSession session)
    {
        _storeMock.Setup(s => s.TryGet(ChatId, out session!)).Returns(true);
        _storeMock.Setup(s => s.Upsert(It.IsAny<AddBirthdayWizardSession>(), It.IsAny<TimeSpan?>()));
        _storeMock.Setup(s => s.Remove(ChatId)).Returns(true);
    }

    private void SetupNoSession()
    {
        AddBirthdayWizardSession? dummy = null;
        _storeMock.Setup(s => s.TryGet(ChatId, out dummy!)).Returns(false);
    }

    private void SetupUser(Language lang = Language.En)
    {
        var user = new BirthdayBot.Domain.Entities.User
        {
            TelegramUserId = UserId,
            Lang           = lang,
            Timezone       = "Europe/Warsaw"
        };
        _usersMock
            .Setup(u => u.GetByTelegramUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
    }

    // ── /add_birthday starts the wizard ──────────────────────────────────────

    [Fact]
    public async Task StartWizard_Creates_Session_And_Sends_Prompt()
    {
        SetupNoSession();
        SetupUser();

        var flow = CreateFlow();
        var handled = await flow.TryHandleAsync(TextUpdate("/add_birthday"));

        handled.Should().BeTrue();
        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(sess => sess.Step == AddWizardStep.Name),
            It.IsAny<TimeSpan?>()), Times.Once);
        _fakeBot.SentRequests.Should().NotBeEmpty(because: "a welcome message should be sent");
    }

    // ── Name step — valid name advances to LastName ───────────────────────────

    [Fact]
    public async Task NameStep_ValidName_Advances_To_LastName()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId) { Step = AddWizardStep.Name };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("Alice"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.Name == "Alice" && ss.Step == AddWizardStep.LastName),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    // ── Name step — single-char name shows error ──────────────────────────────

    [Fact]
    public async Task NameStep_TooShortName_DoesNot_Advance()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId) { Step = AddWizardStep.Name };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("A"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss => ss.Step == AddWizardStep.LastName),
            It.IsAny<TimeSpan?>()), Times.Never);
    }

    // ── Name step — too long name shows error ─────────────────────────────────

    [Fact]
    public async Task NameStep_TooLongName_DoesNot_Advance()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId) { Step = AddWizardStep.Name };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate(new string('X', 65)));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss => ss.Step == AddWizardStep.LastName),
            It.IsAny<TimeSpan?>()), Times.Never);
    }

    // ── LastName step — skip moves to Date ───────────────────────────────────

    [Fact]
    public async Task LastNameStep_Skip_Advances_To_Date()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.LastName, Name = "Alice" };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("➡️ Skip"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.Step == AddWizardStep.Date && ss.LastName == null),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    // ── LastName step — provided value advances to Date ───────────────────────

    [Fact]
    public async Task LastNameStep_ProvidedValue_Saves_LastName_And_Advances()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.LastName, Name = "Alice" };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("Smith"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.LastName == "Smith" && ss.Step == AddWizardStep.Date),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    // ── Date step — text fallback DD.MM.YYYY ─────────────────────────────────

    [Fact]
    public async Task DateStep_ValidTextDate_Advances_To_Relation()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.Date, Name = "Alice" };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("15.03.1990"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.Step == AddWizardStep.Relation &&
                ss.Date == new DateOnly(1990, 3, 15)),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DateStep_DateWithoutYear_DefaultsToCurrentYear()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.Date, Name = "Alice" };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("15.06"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.Date.HasValue &&
                ss.Date!.Value.Month == 6 &&
                ss.Date!.Value.Day   == 15),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DateStep_InvalidText_SendsError_And_Stays_On_Date()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.Date, Name = "Alice" };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("not-a-date-at-all"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss => ss.Step == AddWizardStep.Relation),
            It.IsAny<TimeSpan?>()), Times.Never);
    }

    // ── Calendar callbacks (cal:day:) select a date ───────────────────────────

    [Fact]
    public async Task CalDayCallback_Stores_Date_And_Advances_To_Relation()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.Date, Name = "Alice", CalendarMessageId = 10 };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(CallbackUpdate("cal:day:1990-03-15", msgId: 10));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.Step == AddWizardStep.Relation &&
                ss.Date == new DateOnly(1990, 3, 15)),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    // ── Calendar cancel removes session ───────────────────────────────────────

    [Fact]
    public async Task CalCancelCallback_Removes_Session()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.Date, CalendarMessageId = 5 };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(CallbackUpdate("cal:cancel", msgId: 5));

        _storeMock.Verify(s => s.Remove(ChatId), Times.Once);
    }

    // ── add:cancel removes session ────────────────────────────────────────────

    [Fact]
    public async Task AddCancel_Removes_Session()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId) { Step = AddWizardStep.Confirm };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(CallbackUpdate("add:cancel"));

        _storeMock.Verify(s => s.Remove(ChatId), Times.Once);
    }

    // ── add:save persists birthday ────────────────────────────────────────────

    [Fact]
    public async Task SaveCallback_Creates_Birthday_And_Removes_Session()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
        {
            Step = AddWizardStep.Confirm,
            Name = "Alice",
            Date = new DateOnly(1990, 6, 15)
        };
        SetupSession(session);
        SetupUser();
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<Birthday>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(MongoDB.Bson.ObjectId.GenerateNewId());

        var flow = CreateFlow();
        await flow.TryHandleAsync(CallbackUpdate("add:save"));

        _repoMock.Verify(r => r.CreateAsync(
            It.Is<Birthday>(b => b.Name == "Alice" && b.Date == new DateOnly(1990, 6, 15)),
            It.IsAny<CancellationToken>()), Times.Once);
        _storeMock.Verify(s => s.Remove(ChatId), Times.Once);
    }

    // ── Unrelated update returns false ────────────────────────────────────────

    [Fact]
    public async Task NoSession_And_NoStartCommand_Returns_False()
    {
        SetupNoSession();
        var flow = CreateFlow();

        var handled = await flow.TryHandleAsync(TextUpdate("some random text"));

        handled.Should().BeFalse();
    }

    // ── Relation step — skip advances to Interests ────────────────────────────

    [Fact]
    public async Task RelationStep_Skip_Advances_To_Interests()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.Relation, Name = "Bob" };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("➡️ Skip"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.Relation == null && ss.Step == AddWizardStep.Interests),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    // ── Interests step — skip advances to Confirm ─────────────────────────────

    [Fact]
    public async Task InterestsStep_Skip_Advances_To_Confirm()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
        {
            Step = AddWizardStep.Interests,
            Name = "Bob",
            Date = new DateOnly(1990, 1, 1)
        };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("➡️ Skip"));

        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss =>
                ss.Interests == null && ss.Step == AddWizardStep.Confirm),
            It.IsAny<TimeSpan?>()), Times.AtLeastOnce);
    }

    // ── Cancel text in any step removes session ───────────────────────────────

    [Fact]
    public async Task CancelText_In_NameStep_Removes_Session()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId) { Step = AddWizardStep.Name };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(TextUpdate("❌ Cancel"));

        _storeMock.Verify(s => s.Remove(ChatId), Times.Once);
    }

    // ── Calendar prev/next navigation stays on Date step ─────────────────────

    [Fact]
    public async Task CalNavCallback_Does_Not_Advance_Step()
    {
        var session = new AddBirthdayWizardSession(ChatId, UserId)
            { Step = AddWizardStep.Date, CalendarMessageId = 10 };
        SetupSession(session);
        SetupUser();

        var flow = CreateFlow();
        await flow.TryHandleAsync(CallbackUpdate("cal:prev:2025-05", msgId: 10));

        // Step must remain Date
        _storeMock.Verify(s => s.Upsert(
            It.Is<AddBirthdayWizardSession>(ss => ss.Step == AddWizardStep.Relation),
            It.IsAny<TimeSpan?>()), Times.Never);
    }
}
