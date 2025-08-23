using BirthdayBot.Application.Models;

namespace BirthdayBot.Application.Interfaces;

/// <summary>Хранилище состояний мастеров по chatId, с TTL.</summary>
public interface IConversationSessionStore
{
    bool TryGet(long chatId, out AddBirthdayWizardSession session);
    void Upsert(AddBirthdayWizardSession session, TimeSpan? ttl = null);
    bool Remove(long chatId);
}