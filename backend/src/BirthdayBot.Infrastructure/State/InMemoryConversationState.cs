// path: backend/src/BirthdayBot.Infrastructure/State/InMemoryConversationState.cs
using System.Collections.Concurrent;

namespace BirthdayBot.Infrastructure.State;

public enum ConversationStep
{
    None,
    AddName,
    AddDate,
    AddTimezone
}

public class ConversationContext
{
    public ConversationStep Step { get; set; } = ConversationStep.None;
    public string TempName { get; set; } = "";
    public DateOnly TempDob { get; set; }
}

public class InMemoryConversationState
{
    private readonly ConcurrentDictionary<long, ConversationContext> _state = new();

    public ConversationContext Get(long telegramUserId) =>
        _state.GetOrAdd(telegramUserId, _ => new ConversationContext());

    public void Clear(long telegramUserId) => _state.TryRemove(telegramUserId, out _);
}
