using BirthdayBot.Application.Models;

namespace BirthdayBot.Application.Interfaces;

public interface IAiFeedbackSessionStore
{
    bool TryGet(long chatId, out AiFeedbackSession session);
    void Upsert(AiFeedbackSession session, TimeSpan? ttl = null);
    bool Remove(long chatId);
}
