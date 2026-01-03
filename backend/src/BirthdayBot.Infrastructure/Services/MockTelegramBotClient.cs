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
/// Mock implementation of ITelegramBotClient for local testing.
/// Logs all messages instead of sending them to Telegram API.
/// Stores sent messages in memory for inspection via API endpoint.
/// </summary>
/// <remarks>
/// This class uses explicit interface implementation for events to work around type mismatch issues.
/// The events OnMakingApiRequest and OnApiResponseReceived are never subscribed to in the codebase.
/// </remarks>
public class MockTelegramBotClient
{
    private readonly ILogger<MockTelegramBotClient> _logger;
    private static readonly List<MockMessage> _sentMessages = new();
    private static readonly object _lock = new();
    private IExceptionParser? _exceptionParser;

    public MockTelegramBotClient(ILogger<MockTelegramBotClient> logger)
    {
        _logger = logger;
    }

    public async Task<Message> SendTextMessageAsync(
        ChatId chatId,
        string text,
        ParseMode? parseMode = null,
        IEnumerable<MessageEntity>? entities = null,
        bool? disableWebPagePreview = null,
        bool? disableNotification = null,
        int? replyToMessageId = null,
        bool? allowSendingWithoutReply = null,
        object? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        var message = new MockMessage
        {
            ChatId = chatId.Identifier ?? 0,
            Text = text,
            ParseMode = parseMode?.ToString(),
            ReplyToMessageId = replyToMessageId,
            ReplyMarkup = replyMarkup?.ToString(),
            Timestamp = DateTime.UtcNow
        };

        lock (_lock)
        {
            _sentMessages.Add(message);
            // Keep only last 100 messages
            if (_sentMessages.Count > 100)
            {
                _sentMessages.RemoveAt(0);
            }
        }

        _logger.LogInformation(
            "[MOCK] SendTextMessageAsync: ChatId={ChatId}, Text={Text}, ParseMode={ParseMode}",
            chatId, text, parseMode);

        // Return a mock message
        var mockMessage = new Message
        {
            Date = DateTimeOffset.UtcNow.DateTime,
            Chat = new Chat { Id = chatId.Identifier ?? 0, Type = ChatType.Private },
            Text = text
        };
        // MessageId is read-only, so we can't set it directly
        return await Task.FromResult(mockMessage);
    }

    public async Task<Message> EditMessageTextAsync(
        ChatId chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        IEnumerable<MessageEntity>? entities = null,
        bool? disableWebPagePreview = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MOCK] EditMessageTextAsync: ChatId={ChatId}, MessageId={MessageId}, Text={Text}",
            chatId, messageId, text);

        var mockMessage = new Message
        {
            Date = DateTimeOffset.UtcNow.DateTime,
            Chat = new Chat { Id = chatId.Identifier ?? 0, Type = ChatType.Private },
            Text = text
        };
        // MessageId is read-only, so we can't set it directly
        return await Task.FromResult(mockMessage);
    }

    public async Task<bool> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        bool? showAlert = null,
        string? url = null,
        int? cacheTime = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MOCK] AnswerCallbackQueryAsync: CallbackQueryId={CallbackQueryId}, Text={Text}",
            callbackQueryId, text);

        return await Task.FromResult(true);
    }

    public async Task<bool> SetMyCommandsAsync(
        IEnumerable<BotCommand> commands,
        BotCommandScope? scope = null,
        string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[MOCK] SetMyCommandsAsync: Commands={Count}",
            commands.Count());

        return await Task.FromResult(true);
    }

    public Task<TResponse> MakeRequestAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] MakeRequestAsync: Request={RequestType}", request.GetType().Name);
        return Task.FromResult<TResponse>(default!);
    }

    public Task<TResponse> MakeRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] MakeRequest: Request={RequestType}", request.GetType().Name);
        return Task.FromResult<TResponse>(default!);
    }

    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] SendRequest: Request={RequestType}", request.GetType().Name);
        return Task.FromResult<TResponse>(default!);
    }

    public long? BotId => 123456789; // Mock bot ID

    public Task<bool> TestApiAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] TestApiAsync");
        return Task.FromResult(true);
    }

    public Task DownloadFileAsync(string filePath, Stream destination, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] DownloadFileAsync: FilePath={FilePath}", filePath);
        return Task.CompletedTask;
    }

    public Task DownloadFileAsync(Telegram.Bot.Types.File file, Stream destination, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] DownloadFileAsync: File={FileId}", file?.FileId);
        return Task.CompletedTask;
    }

    public bool LocalBotServer => false;

    public TimeSpan Timeout
    {
        get => TimeSpan.FromSeconds(30);
        set { /* Mock - no-op */ }
    }

    public IExceptionParser ExceptionsParser
    {
        get => _exceptionParser!;
        set => _exceptionParser = value;
    }


    // Static method to get sent messages for API endpoint
    public static List<MockMessage> GetSentMessages(long? chatId = null)
    {
        lock (_lock)
        {
            if (chatId.HasValue)
            {
                return _sentMessages.Where(m => m.ChatId == chatId.Value).ToList();
            }
            return _sentMessages.ToList();
        }
    }

    public static void ClearMessages()
    {
        lock (_lock)
        {
            _sentMessages.Clear();
        }
    }
}

/// <summary>
/// Represents a message sent by the mock bot client
/// </summary>
public class MockMessage
{
    public long ChatId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ParseMode { get; set; }
    public int? ReplyToMessageId { get; set; }
    public string? ReplyMarkup { get; set; }
    public DateTime Timestamp { get; set; }
}
