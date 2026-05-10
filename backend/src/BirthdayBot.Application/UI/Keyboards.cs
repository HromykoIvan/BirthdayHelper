using BirthdayBot.Application.Interfaces;
using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace BirthdayBot.Application.UI;

public static class Keyboards
{
    private static ILocalizationService? _i18n;

    // Backward-compatible keyboards used by legacy flow implementation.
    public static ReplyKeyboardMarkup DateKb => new(new[]
    {
        new KeyboardButton[] { "Сегодня", "Завтра" },
        new KeyboardButton[] { "❌ Отмена" }
    })
    { ResizeKeyboard = true, OneTimeKeyboard = true };

    public static ReplyKeyboardMarkup TimeZoneKb => new(new[]
    {
        new KeyboardButton[] { "🔎 Ввести город", "➡️ Пропустить" },
        new KeyboardButton[] { "❌ Отмена" }
    })
    { ResizeKeyboard = true, OneTimeKeyboard = true };

    public static InlineKeyboardMarkup ConfirmKb => new(new[]
    {
        new [] { InlineKeyboardButton.WithCallbackData("✅ Сохранить", "add:ok") },
        new [] { InlineKeyboardButton.WithCallbackData("✏️ Изменить", "add:edit") },
        new [] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "add:cancel") }
    });

    public static void Initialize(ILocalizationService i18n)
    {
        _i18n = i18n;
    }

    private static string GetText(Language lang, string key)
    {
        return _i18n?.GetText(lang, key) ?? key;
    }

    // ── Main menu (shown on /start) ──

    public static InlineKeyboardMarkup MainMenuKb(Language lang)
    {
        return new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(GetText(lang, "menu_add"), "menu:add"),
                InlineKeyboardButton.WithCallbackData(GetText(lang, "menu_list"), "menu:list"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(GetText(lang, "menu_settings"), "menu:settings"),
                InlineKeyboardButton.WithCallbackData(GetText(lang, "menu_help"), "menu:help"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(GetText(lang, "menu_import"), "menu:import_google"),
            }
        });
    }

    // ── "Back to main menu" single button ──

    public static InlineKeyboardMarkup BackToMenuKb(Language lang)
    {
        return new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(GetText(lang, "back_to_menu"), "menu:home") }
        });
    }

    // ── Language selection ──

    public static InlineKeyboardMarkup LanguageSelectionKb(string callbackPrefix = "lang:")
    {
        return new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🇷🇺 Русский", $"{callbackPrefix}ru"),
                InlineKeyboardButton.WithCallbackData("🇵🇱 Polski", $"{callbackPrefix}pl"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🇬🇧 English", $"{callbackPrefix}en"),
            }
        });
    }

    // ── Reply keyboards for wizard steps ──

    public static ReplyKeyboardMarkup SkipCancelKb(Language lang)
    {
        return new(new[]
        {
            new KeyboardButton[] { GetText(lang, "skip"), GetText(lang, "cancel") }
        })
        { ResizeKeyboard = true, OneTimeKeyboard = true };
    }

    public static ReplyKeyboardMarkup RelationKb(Language lang)
    {
        return new(new[]
        {
            new KeyboardButton[] { GetText(lang, "relation_family"), GetText(lang, "relation_partner") },
            new KeyboardButton[] { GetText(lang, "relation_friend"), GetText(lang, "relation_colleague") },
            new KeyboardButton[] { GetText(lang, "relation_other"), GetText(lang, "skip") },
            new KeyboardButton[] { GetText(lang, "cancel") }
        })
        { ResizeKeyboard = true, OneTimeKeyboard = true };
    }

    // ── Settings keyboards ──

    public static InlineKeyboardMarkup SettingsKb(Language lang, User user)
    {
        var rows = new List<InlineKeyboardButton[]>();

        // Notification time
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"🕐 {user.NotifyAtLocalTime}",
                "settings:time")
        });

        // Interface language
        var langName = user.Lang switch
        {
            Language.Ru => GetText(lang, "lang_russian"),
            Language.Pl => GetText(lang, "lang_polish"),
            Language.En => GetText(lang, "lang_english"),
            _ => user.Lang.ToString()
        };
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"🌐 {langName}",
                "settings:lang")
        });

        // Timezone
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"🌍 {user.Timezone}",
                "settings:tz")
        });

        // Auto-greetings
        var autoStatus = user.AutoGenerateGreetings
            ? GetText(lang, "settings_on")
            : GetText(lang, "settings_off");
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"🎉 {autoStatus}",
                "settings:auto")
        });

        // Tone
        var toneName = user.Tone == Tone.Formal
            ? GetText(lang, "settings_formal")
            : GetText(lang, "settings_friendly");
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"💬 {toneName}",
                "settings:tone")
        });

        // Back to menu
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                GetText(lang, "back_to_menu"),
                "menu:home")
        });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup TimePickerKb(Language lang)
    {
        var rows = new List<InlineKeyboardButton[]>();

        // Quick time buttons
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("08:00", "settings:time:08:00"),
            InlineKeyboardButton.WithCallbackData("09:00", "settings:time:09:00"),
            InlineKeyboardButton.WithCallbackData("10:00", "settings:time:10:00"),
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("12:00", "settings:time:12:00"),
            InlineKeyboardButton.WithCallbackData("15:00", "settings:time:15:00"),
            InlineKeyboardButton.WithCallbackData("18:00", "settings:time:18:00"),
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("20:00", "settings:time:20:00"),
            InlineKeyboardButton.WithCallbackData("21:00", "settings:time:21:00"),
            InlineKeyboardButton.WithCallbackData("22:00", "settings:time:22:00"),
        });

        // Back button
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ " + GetText(lang, "back_to_menu"), "menu:settings")
        });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup ToneToggleKb(Language lang, Tone current)
    {
        var rows = new List<InlineKeyboardButton[]>();

        if (current == Tone.Formal)
        {
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"✅ {GetText(lang, "settings_formal")}",
                    "settings:tone")
            });
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GetText(lang, "settings_friendly"),
                    "settings:tone")
            });
        }
        else
        {
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GetText(lang, "settings_formal"),
                    "settings:tone")
            });
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"✅ {GetText(lang, "settings_friendly")}",
                    "settings:tone")
            });
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ " + GetText(lang, "back_to_menu"), "menu:settings")
        });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup CommonTimezonesKb(Language lang)
    {
        var rows = new List<InlineKeyboardButton[]>();

        // Common European timezones
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("Europe/Warsaw", "settings:tz:Europe/Warsaw"),
            InlineKeyboardButton.WithCallbackData("Europe/Moscow", "settings:tz:Europe/Moscow"),
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("Europe/Kiev", "settings:tz:Europe/Kiev"),
            InlineKeyboardButton.WithCallbackData("Europe/Berlin", "settings:tz:Europe/Berlin"),
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("Europe/London", "settings:tz:Europe/London"),
            InlineKeyboardButton.WithCallbackData("America/New_York", "settings:tz:America/New_York"),
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(GetText(lang, "calendar_manual"), "settings:tz:input")
        });

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("◀️ " + GetText(lang, "back_to_menu"), "menu:settings")
        });

        return new InlineKeyboardMarkup(rows);
    }

    // ── Upcoming period filter (fixed callback data) ──

    public static InlineKeyboardMarkup UpcomingKb(Language lang)
    {
        return new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(GetText(lang, "upcoming_today"), "up:today"),
                InlineKeyboardButton.WithCallbackData(GetText(lang, "upcoming_tomorrow"), "up:tomorrow"),
                InlineKeyboardButton.WithCallbackData(GetText(lang, "upcoming_7_days"), "up:7")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(GetText(lang, "upcoming_this_month"), "up:this"),
                InlineKeyboardButton.WithCallbackData(GetText(lang, "upcoming_next_month"), "up:next")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(GetText(lang, "all_records_button"), "up:all")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(GetText(lang, "back_to_menu"), "menu:home")
            }
        });
    }
}
