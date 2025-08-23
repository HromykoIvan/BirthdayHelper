namespace BirthdayBot.Infrastructure.Options;

public class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://mongodb:27017/birthdays";
    public string Database { get; set; } = "birthdays";
}
