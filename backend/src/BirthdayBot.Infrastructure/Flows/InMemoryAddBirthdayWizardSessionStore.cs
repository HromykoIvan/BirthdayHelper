using System.Collections.Concurrent;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Application.Models;

namespace BirthdayBot.Infrastructure.Flows;

public sealed class InMemoryAddBirthdayWizardSessionStore : IAddBirthdayWizardSessionStore
{
    private readonly ConcurrentDictionary<(long ChatId,long UserId), AddBirthdayWizardSession> _map = new();

    public AddBirthdayWizardSession GetOrCreate(long chatId, long userId)
        => _map.GetOrAdd((chatId,userId), _ => new AddBirthdayWizardSession(chatId, userId));

    public AddBirthdayWizardSession? Get(long chatId, long userId)
        => _map.TryGetValue((chatId,userId), out var s) ? s : null;

    public void Upsert(AddBirthdayWizardSession session)
        => _map[(session.ChatId, session.UserId)] = session;

    public void Remove(long chatId, long userId)
        => _map.TryRemove((chatId,userId), out _);
}