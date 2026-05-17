using BirthdayBot.Application.Models;
using BirthdayBot.Domain.Entities;

namespace BirthdayBot.Application.Interfaces;

public interface IIntentRouter
{
    Task<IntentParseResult> ParseAsync(User user, string input, CancellationToken ct = default);
}
