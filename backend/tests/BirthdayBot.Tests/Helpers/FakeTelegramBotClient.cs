using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;

namespace BirthdayBot.Tests.Helpers;

/// <summary>
/// Minimal implementation of <see cref="ITelegramBotClient"/> for unit tests.
/// Records every <see cref="SendRequest{TResponse}"/> call and returns safe defaults.
/// </summary>
public sealed class FakeTelegramBotClient : ITelegramBotClient
{
    private static readonly Message DefaultMessage = new() { Chat = new Chat { Id = 0 } };

    public ConcurrentBag<string> SentRequests { get; } = new();

    // ── ITelegramBotClient ────────────────────────────────────────────────────

    public Task<TResponse> SendRequest<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        SentRequests.Add(request.GetType().Name);
        if (typeof(TResponse) == typeof(Message))
            return Task.FromResult((TResponse)(object)DefaultMessage);
        if (typeof(TResponse) == typeof(bool))
            return Task.FromResult((TResponse)(object)true);
        return Task.FromResult<TResponse>(default!);
    }

    public Task<TResponse> MakeRequestAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        SendRequest(request, cancellationToken);

    public Task<bool> TestApiAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task DownloadFileAsync(string filePath, Stream destination,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    // Properties
    public bool           LocalBotServer  { get; set; } = false;
    public long?          BotId           { get; }      = 12345;
    public TimeSpan       Timeout         { get; set; } = TimeSpan.FromSeconds(30);
    public IExceptionParser ExceptionsParser { get; set; } = new DefaultExceptionParser();

    // Events — unused in tests, but required by interface
#pragma warning disable CS0067
    public event AsyncEventHandler<ApiRequestEventArgs>?  OnMakingApiRequest;
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived;
#pragma warning restore CS0067

    // ── Helper assertions ─────────────────────────────────────────────────────

    public bool WasCalled(string requestTypeName) => SentRequests.Contains(requestTypeName);
    public int  CountOf(string requestTypeName)   => SentRequests.Count(r => r == requestTypeName);
}
