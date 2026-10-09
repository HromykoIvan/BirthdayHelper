using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Services;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Infrastructure.Mongo;
using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using User = BirthdayBot.Domain.Entities.User;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// One-time VK ID OAuth 2.1 / PKCE import of accessible VK friends' birthdays.
/// Tokens are consumed in-memory during a single callback; no token is persisted.
/// </summary>
public sealed class VkImportService
{
    private readonly VkImportOptions _options;
    private readonly IMongoCollection<VkAuthorizationSessionDocument> _states;
    private readonly IUserRepository _users;
    private readonly TelegramBirthdayImportService _imports;
    private readonly ITelegramBotClient _bot;
    private readonly HttpClient _http;
    private readonly ILogger<VkImportService> _logger;

    public VkImportService(
        IOptions<VkImportOptions> options,
        MongoContext db,
        IUserRepository users,
        TelegramBirthdayImportService imports,
        ITelegramBotClient bot,
        HttpClient http,
        ILogger<VkImportService> logger)
    {
        _options = options.Value;
        _states = db.VkAuthorizationSessions;
        _users = users;
        _imports = imports;
        _bot = bot;
        _http = http;
        _logger = logger;
    }

    public bool IsEnabled => _options.IsConfigured;

    public async Task BeginAsync(User user, CallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message?.Chat.Id ?? 0;
        if (chatId != callback.From.Id || !IsEnabled)
        {
            await _bot.AnswerCallbackQueryAsync(
                callback.Id, "VK import is not configured yet.", cancellationToken: ct);
            return;
        }

        var state = RandomUrlSafe(32);
        var verifier = RandomUrlSafe(64);
        var challenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        await _states.InsertOneAsync(new VkAuthorizationSessionDocument
        {
            State = state,
            TelegramUserId = callback.From.Id,
            ChatId = chatId,
            CodeVerifier = verifier,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        }, cancellationToken: ct);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = _options.Scope
        };
        var loginUrl = "https://id.vk.ru/authorize?" +
                       string.Join("&", query.Select(x =>
                           $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

        await _bot.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);
        await _bot.SendTextMessageAsync(
            chatId,
            user.Lang switch
            {
                BirthdayBot.Domain.Enums.Language.Ru =>
                    "🔗 <b>Импорт из ВКонтакте</b>\n\n" +
                    "Открой ссылку, войди через VK ID и предоставь доступ к списку друзей. " +
                    "Я получу только доступные имена и даты рождения, после чего покажу их перед импортом. " +
                    "Не передавай мне пароль или токен. Ссылка действует 10 минут.",
                BirthdayBot.Domain.Enums.Language.Pl =>
                    "🔗 <b>Import z VK</b>\n\nZaloguj się przez VK ID. Zostaną pobrane tylko dostępne imiona i daty urodzin. " +
                    "Dane zostaną pokazane przed importem. Link jest ważny przez 10 minut.",
                _ =>
                    "🔗 <b>VK birthday import</b>\n\nSign in through VK ID. " +
                    "Only visible names and birthday dates will be previewed. " +
                    "Do not share your password or token. This link expires in 10 minutes."
            },
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithUrl("🔐 VK ID", loginUrl)
            }),
            cancellationToken: ct);
    }

    public async Task<bool> CompleteAsync(
        string? code, string? state, string? deviceId,
        CancellationToken ct)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(state))
            return false;

        // Atomically consume state: prevents callback replay or parallel imports.
        var session = await _states.FindOneAndDeleteAsync(x =>
            x.State == state && x.ExpiresAtUtc > DateTime.UtcNow, ct);
        if (session is null)
            return false;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(deviceId))
        {
            await SafeNotifyFailureAsync(session.ChatId,
                "VK ID did not return the expected authorization details. Start the import again.");
            return false;
        }

        try
        {
            var user = await _users.GetByTelegramUserIdAsync(session.TelegramUserId, ct);
            if (user is null)
                return false;

            var accessToken = await ExchangeCodeAsync(
                code, state, deviceId, session.CodeVerifier, ct);

            // No refresh token is stored: every import requires explicit VK authorization.
            var entries = new List<ContactImportParser.Entry>();
            var withoutBirthday = 0;
            var invalid = 0;
            const int pageSize = 500;

            for (var offset = 0; offset < ContactImportParser.MaxContacts; offset += pageSize)
            {
                var page = await FetchFriendsPageAsync(accessToken, offset, pageSize, ct);
                entries.AddRange(page.Entries);
                withoutBirthday += page.WithoutBirthday;
                invalid += page.Invalid;

                if (offset + pageSize >= page.TotalFriends)
                    break;
            }

            await _imports.StartPreviewAsync(
                user, session.ChatId, "vk",
                new ContactImportParser.Result(
                    entries, withoutBirthday, invalid,
                    entries.Count + withoutBirthday + invalid),
                ct);
            return true;
        }
        catch (VkApiException ex)
        {
            _logger.LogWarning("VK friends API refused import with code {VkErrorCode}.", ex.ApiErrorCode);
            await SafeNotifyFailureAsync(session.ChatId,
                "VK did not allow access to friends. Check the VK ID application's friends permission or try again.");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("VK ID import failed at network or response parsing stage.");
            await SafeNotifyFailureAsync(session.ChatId,
                "Could not load birthdays from VK. Try again later.");
            return false;
        }
    }

    private async Task<string> ExchangeCodeAsync(
        string code, string state, string deviceId, string verifier,
        CancellationToken ct)
    {
        using var response = await _http.PostAsync(
            "https://id.vk.ru/oauth2/auth",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = _options.ClientId,
                ["redirect_uri"] = _options.RedirectUri,
                ["code_verifier"] = verifier,
                ["code"] = code,
                ["state"] = state,
                ["device_id"] = deviceId
            }), ct);
        if (!response.IsSuccessStatusCode)
            throw new VkApiException((int)response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var tokenElement) ||
            tokenElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tokenElement.GetString()))
            throw new JsonException("VK ID token response is missing access_token.");

        if (root.TryGetProperty("state", out var returnedState) &&
            returnedState.ValueKind == JsonValueKind.String &&
            !string.Equals(returnedState.GetString(), state, StringComparison.Ordinal))
            throw new JsonException("VK ID OAuth state mismatch.");

        return tokenElement.GetString()!;
    }

    private async Task<VkFriendsParser.Page> FetchFriendsPageAsync(
        string accessToken, int offset, int count, CancellationToken ct)
    {
        // POST body prevents the access token appearing in request URLs or reverse-proxy logs.
        using var response = await _http.PostAsync(
            "https://api.vk.com/method/friends.get",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["access_token"] = accessToken,
                ["v"] = _options.ApiVersion,
                ["fields"] = "bdate",
                ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }), ct);

        response.EnsureSuccessStatusCode();
        return VkFriendsParser.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private async Task SafeNotifyFailureAsync(long chatId, string message)
    {
        try
        {
            await _bot.SendTextMessageAsync(chatId, message, cancellationToken: CancellationToken.None);
        }
        catch (Exception)
        {
            _logger.LogWarning("Unable to notify Telegram user about failed VK import.");
        }
    }

    private static string RandomUrlSafe(int bytes) =>
        ToBase64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
