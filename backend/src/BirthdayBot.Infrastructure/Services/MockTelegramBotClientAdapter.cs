using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Adapter that inherits from TelegramBotClient to get correct event types,
/// but overrides methods to delegate to MockTelegramBotClient for actual functionality.
/// This works around type mismatch issues with event types.
/// </summary>
public class MockTelegramBotClientAdapter : TelegramBotClient
{
    private readonly MockTelegramBotClient _mockClient;

    public MockTelegramBotClientAdapter(ILogger<MockTelegramBotClient> logger) 
        : base("123456789:ABCdefGHIjklMNOpqrsTUVwxyz") // Dummy token - won't be used
    {
        _mockClient = new MockTelegramBotClient(logger);
    }

    // Hide base class methods and delegate to mock client
    public new Task<Message> SendTextMessageAsync(ChatId chatId, string text, ParseMode? parseMode = null, IEnumerable<MessageEntity>? entities = null, bool? disableWebPagePreview = null, bool? disableNotification = null, int? replyToMessageId = null, bool? allowSendingWithoutReply = null, object? replyMarkup = null, CancellationToken cancellationToken = default)
        => _mockClient.SendTextMessageAsync(chatId, text, parseMode, entities, disableWebPagePreview, disableNotification, replyToMessageId, allowSendingWithoutReply, replyMarkup, cancellationToken);

    public new Task<Message> EditMessageTextAsync(ChatId chatId, int messageId, string text, ParseMode? parseMode = null, IEnumerable<MessageEntity>? entities = null, bool? disableWebPagePreview = null, InlineKeyboardMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
        => _mockClient.EditMessageTextAsync(chatId, messageId, text, parseMode, entities, disableWebPagePreview, replyMarkup, cancellationToken);

    public new Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool? showAlert = null, string? url = null, int? cacheTime = null, CancellationToken cancellationToken = default)
        => _mockClient.AnswerCallbackQueryAsync(callbackQueryId, text, showAlert, url, cacheTime, cancellationToken);

    public new Task<bool> SetMyCommandsAsync(IEnumerable<BotCommand> commands, BotCommandScope? scope = null, string? languageCode = null, CancellationToken cancellationToken = default)
        => _mockClient.SetMyCommandsAsync(commands, scope, languageCode, cancellationToken);

    public new Task<TResponse> MakeRequestAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => _mockClient.MakeRequestAsync<TResponse>(request, cancellationToken);

    public new Task<TResponse> MakeRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => _mockClient.MakeRequest<TResponse>(request, cancellationToken);

    public new Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => _mockClient.SendRequest<TResponse>(request, cancellationToken);

    public new long? BotId => _mockClient.BotId;

    public new Task<bool> TestApiAsync(CancellationToken cancellationToken = default)
        => _mockClient.TestApiAsync(cancellationToken);

    public new Task DownloadFileAsync(string filePath, Stream destination, CancellationToken cancellationToken = default)
        => _mockClient.DownloadFileAsync(filePath, destination, cancellationToken);

    public new Task DownloadFileAsync(Telegram.Bot.Types.File file, Stream destination, CancellationToken cancellationToken = default)
        => _mockClient.DownloadFileAsync(file, destination, cancellationToken);

    public new bool LocalBotServer => _mockClient.LocalBotServer;

    public new TimeSpan Timeout
    {
        get => _mockClient.Timeout;
        set => _mockClient.Timeout = value;
    }

    public new IExceptionParser ExceptionsParser
    {
        get => _mockClient.ExceptionsParser;
        set => _mockClient.ExceptionsParser = value;
    }

    // Events are inherited from TelegramBotClient with correct types - no need to override
}

