using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BirthdayBot.Api.Hosted;

public class RegisterBotCommandsHostedService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<RegisterBotCommandsHostedService> _logger;

    public RegisterBotCommandsHostedService(
        ITelegramBotClient bot,
        ILogger<RegisterBotCommandsHostedService> logger)
    {
        _bot = bot;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var commands = new[]
        {
            new BotCommand { Command = "start",         Description = "Приветствие" },
            new BotCommand { Command = "add_birthday",  Description = "Добавить день рождения" },
            new BotCommand { Command = "list",          Description = "Показать список" },
            new BotCommand { Command = "remove",        Description = "Удалить запись" },
            new BotCommand { Command = "settings",      Description = "Настройки" },
            new BotCommand { Command = "help",          Description = "Помощь" },
        };

        await _bot.SetMyCommandsAsync(commands, cancellationToken: stoppingToken);
        _logger.LogInformation("Telegram bot commands registered ({Count})", commands.Length);
    }
}