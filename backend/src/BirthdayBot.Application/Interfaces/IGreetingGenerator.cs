using BirthdayBot.Domain.Entities;
using BirthdayBot.Domain.Enums;

namespace BirthdayBot.Application.Interfaces;

public interface IGreetingGenerator
{
    string Generate(Language lang, Tone tone, string name, int age);
    string GeneratePersonalized(User user, Birthday birthday, int age);
}
