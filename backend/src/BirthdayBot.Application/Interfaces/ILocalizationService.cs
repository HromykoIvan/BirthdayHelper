using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Application.Interfaces;

public interface ILocalizationService
{
    string GetText(Language lang, string key);
}