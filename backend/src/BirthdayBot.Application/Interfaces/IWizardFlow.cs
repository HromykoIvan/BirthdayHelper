using Telegram.Bot.Types;

namespace BirthdayBot.Application.Interfaces;

/// <summary>
/// Обработчик «пошагового мастера» для конкретного сценария (например, добавление ДР).
/// Возвращает true, если обновление обработано и дальше по пайплайну идти не нужно.
/// </summary>
public interface IWizardFlow
{
    Task<bool> TryHandleAsync(Update update, CancellationToken ct = default);
}