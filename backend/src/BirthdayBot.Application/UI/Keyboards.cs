using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.UI;

public static class Keyboards
{
    // ── Main menu (shown on /start) ──

    public static readonly InlineKeyboardMarkup MainMenuKb =
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🎂 Добавить ДР", "menu:add"),
                InlineKeyboardButton.WithCallbackData("📋 Мои записи",  "menu:list"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "menu:settings"),
                InlineKeyboardButton.WithCallbackData("❓ Помощь",    "menu:help"),
            }
        });

    // ── "Back to main menu" single button ──

    public static readonly InlineKeyboardMarkup BackToMenuKb =
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "menu:home") }
        });

    // ── Reply keyboards for wizard steps ──

    public static readonly ReplyKeyboardMarkup DateKb =
        new(new[] {
            new KeyboardButton[] { "Сегодня", "Завтра" },
            new KeyboardButton[] { "➡️ Пропустить", "❌ Отмена" }
        }) { ResizeKeyboard = true, OneTimeKeyboard = true };

    public static readonly ReplyKeyboardMarkup TimeZoneKb =
        new(new[] {
            new KeyboardButton[] { new("📍 Отправить геопозицию") { RequestLocation = true } },
            new KeyboardButton[] { "🔎 Ввести город", "➡️ Пропустить" },
            new KeyboardButton[] { "❌ Отмена" }
        }) { ResizeKeyboard = true, OneTimeKeyboard = true };

    public static readonly ReplyKeyboardMarkup RelationKb =
        new(new[] {
            new KeyboardButton[] { "👪 Семья", "❤️ Партнёр" },
            new KeyboardButton[] { "🎓 Друг", "💼 Коллега" },
            new KeyboardButton[] { "Другое", "➡️ Пропустить" },
            new KeyboardButton[] { "❌ Отмена" }
        }) { ResizeKeyboard = true, OneTimeKeyboard = true };

    public static readonly ReplyKeyboardMarkup SkipCancelKb =
        new(new[] {
            new KeyboardButton[] { "➡️ Пропустить", "❌ Отмена" }
        }) { ResizeKeyboard = true, OneTimeKeyboard = true };

    // ── Upcoming period filter (fixed callback data) ──

    public static readonly InlineKeyboardMarkup UpcomingKb =
        new(new[] {
            new [] {
                InlineKeyboardButton.WithCallbackData("📅 Сегодня", "up:today"),
                InlineKeyboardButton.WithCallbackData("➡️ Завтра",  "up:tomorrow"),
                InlineKeyboardButton.WithCallbackData("🗓 7 дней",  "up:7")
            },
            new [] {
                InlineKeyboardButton.WithCallbackData("📆 Этот месяц",  "up:this"),
                InlineKeyboardButton.WithCallbackData("📆 След. месяц", "up:next")
            },
            new [] {
                InlineKeyboardButton.WithCallbackData("📑 Все записи", "up:all")
            },
            new [] {
                InlineKeyboardButton.WithCallbackData("🏠 Меню", "menu:home")
            }
        });

    // ── Wizard confirm keyboard ──

    public static readonly InlineKeyboardMarkup ConfirmKb =
        new(new[] {
            new[] {
                InlineKeyboardButton.WithCallbackData("✅ Сохранить", "add:ok"),
                InlineKeyboardButton.WithCallbackData("✏️ Изменить", "add:edit"),
                InlineKeyboardButton.WithCallbackData("❌ Отмена",   "add:cancel")
            }
        });
}
