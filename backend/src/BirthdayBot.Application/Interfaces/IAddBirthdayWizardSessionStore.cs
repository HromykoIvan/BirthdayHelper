using BirthdayBot.Application.Models;

namespace BirthdayBot.Application.Interfaces;

public interface IAddBirthdayWizardSessionStore
{
    AddBirthdayWizardSession GetOrCreate(long chatId, long userId);
    AddBirthdayWizardSession? Get(long chatId, long userId);
    void Upsert(AddBirthdayWizardSession session);
    void Remove(long chatId, long userId);
}