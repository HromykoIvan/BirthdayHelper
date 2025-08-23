using Telegram.Bot.Types;

namespace BirthdayBot.Application.Interfaces;

public interface IUpdateHandler
{
    Task HandleUpdateAsync(Update update, CancellationToken ct);
}