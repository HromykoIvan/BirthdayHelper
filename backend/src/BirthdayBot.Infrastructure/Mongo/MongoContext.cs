using Microsoft.Extensions.Options;
using MongoDB.Driver;
using BirthdayBot.Api.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using BirthdayBot.Domain.Entities;
using MongoDB.Bson.Serialization.IdGenerators;

namespace BirthdayBot.Infrastructure.Mongo;

public class MongoContext
{
    public IMongoDatabase Database { get; }
    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
    public IMongoCollection<Birthday> Birthdays => Database.GetCollection<Birthday>("birthdays");
    public IMongoCollection<DeliveryLog> DeliveryLogs => Database.GetCollection<DeliveryLog>("delivery_logs");

    public MongoContext(IOptions<MongoOptions> options)
    {
        var mongo = new MongoClient(options.Value.ConnectionString);
        Database = mongo.GetDatabase(options.Value.Database);

        // Register serializers once
        if (!BsonClassMap.IsClassMapRegistered(typeof(User)))
        {
            BsonSerializer.RegisterSerializer(new DateTimeSerializer(DateTimeKind.Utc));
            BsonClassMap.RegisterClassMap<User>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(x => x.Id).SetIdGenerator(ObjectIdGenerator.Instance);
                cm.GetMemberMap(x => x.CreatedAt).SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
            });
            BsonClassMap.RegisterClassMap<Birthday>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(x => x.Id).SetIdGenerator(ObjectIdGenerator.Instance);
            });
            BsonClassMap.RegisterClassMap<DeliveryLog>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(x => x.Id).SetIdGenerator(ObjectIdGenerator.Instance);
            });
        }

        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        // Users: unique TelegramUserId
        var usersIdx = new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.TelegramUserId),
            new CreateIndexOptions { Unique = true, Name = "ux_users_telegram_id" });
        Users.Indexes.CreateOne(usersIdx);

        // Birthdays: by UserId, optional uniqueness by (UserId, Name)
        var bUserIdx = new CreateIndexModel<Birthday>(Builders<Birthday>.IndexKeys.Ascending(b => b.UserId),
            new CreateIndexOptions { Name = "ix_birthdays_user" });
        Birthdays.Indexes.CreateOne(bUserIdx);

        var bUniqueName = new CreateIndexModel<Birthday>(
            Builders<Birthday>.IndexKeys.Ascending(b => b.UserId).Ascending(b => b.Name),
            new CreateIndexOptions { Name = "ux_birthdays_user_name", Unique = false } // set to true if you want strict uniqueness
        );
        Birthdays.Indexes.CreateOne(bUniqueName);

        var logsIdx = new CreateIndexModel<DeliveryLog>(Builders<DeliveryLog>.IndexKeys.Ascending(l => l.UserId),
            new CreateIndexOptions { Name = "ix_logs_user" });
        DeliveryLogs.Indexes.CreateOne(logsIdx);
    }
}
