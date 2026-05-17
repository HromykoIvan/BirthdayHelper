using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;
using BirthdayBot.Application.Models;

namespace BirthdayBot.Application.Interfaces;

public interface IAiGreetingEnhancer
{
    Task<AiEnhanceResult> EnhanceAsync(
        User user,
        Birthday birthday,
        string draftGreeting,
        int age,
        CancellationToken ct = default);
}
