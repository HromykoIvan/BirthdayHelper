using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Application.Interfaces;

public interface IAiGreetingEnhancer
{
    Task<string> EnhanceAsync(
        User user,
        Birthday birthday,
        string draftGreeting,
        int age,
        CancellationToken ct = default);
}
