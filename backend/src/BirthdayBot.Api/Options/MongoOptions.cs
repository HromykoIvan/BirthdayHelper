// path: backend/src/BirthdayBot.Api/Options/MongoOptions.cs
namespace BirthdayBot.Api.Options;

public class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://mongodb:27017/birthdays";
    public string Database { get; set; } = "birthdays";
}