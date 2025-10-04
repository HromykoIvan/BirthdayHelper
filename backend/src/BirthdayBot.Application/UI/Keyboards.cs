using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.UI;

public static class Keyboards
{
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
            new KeyboardButton[] { "Другое", "❌ Отмена" }
        }) { ResizeKeyboard = true, OneTimeKeyboard = true };

    public static readonly InlineKeyboardMarkup UpcomingKb =
        new(new[] {
            new [] {
                InlineKeyboardButton.WithCallbackData("Сегодня", "up:today"),
                InlineKeyboardButton.WithCallbackData("Завтра",  "up:tomorrow"),
                InlineKeyboardButton.WithCallbackData("7 дней",  "up:7")
            },
            new [] {
                InlineKeyboardButton.WithCallbackData("Этот месяц", "up:mon:this"),
                InlineKeyboardButton.WithCallbackData("След. месяц","up:mon:next")
            }
        });

    public static readonly InlineKeyboardMarkup ConfirmKb =
        new(new[] {
            new[] {
                InlineKeyboardButton.WithCallbackData("✅ Сохранить", "add:ok"),
                InlineKeyboardButton.WithCallbackData("✏️ Изменить", "add:edit"),
                InlineKeyboardButton.WithCallbackData("❌ Отмена",   "add:cancel")
            }
        });

    public static InlineKeyboardMarkup AddUpcomingFilter()
        => new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📅 Сегодня", "up:today"),
                InlineKeyboardButton.WithCallbackData("➡️ Завтра", "up:tomorrow"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🗓 7 дней", "up:7"),
                InlineKeyboardButton.WithCallbackData("📆 Этот месяц", "up:this"),
                InlineKeyboardButton.WithCallbackData("📆 След. месяц", "up:next"),
            }
        });
}