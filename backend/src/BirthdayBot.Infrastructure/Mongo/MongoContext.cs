using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using BirthdayBot.Domain.Entities;
using MongoDB.Bson.Serialization.IdGenerators;
using BirthdayBot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace BirthdayBot.Infrastructure.Mongo;

public class MongoContext
{
    private readonly ILogger<MongoContext> _logger;
    private readonly SemaphoreSlim _indexesLock = new(1, 1);
    private volatile bool _indexesEnsured;

    public IMongoDatabase Database { get; }
    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
    public IMongoCollection<Birthday> Birthdays => Database.GetCollection<Birthday>("birthdays");
    public IMongoCollection<DeliveryLog> DeliveryLogs => Database.GetCollection<DeliveryLog>("delivery_logs");

    public MongoContext(IOptions<MongoOptions> options, ILogger<MongoContext> logger)
    {
        _logger = logger;
        var conn = options.Value.ConnectionString;
        var mongo = new MongoClient(conn);
        Database = mongo.GetDatabase(options.Value.Database);

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
    }

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        if (_indexesEnsured)
        {
            return;
        }

        await _indexesLock.WaitAsync(ct);
        try
        {
            if (_indexesEnsured)
            {
                return;
            }

        var usersIdx = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.TelegramUserId),
            new CreateIndexOptions { Unique = true, Name = "ux_users_telegram_id" });
            await Users.Indexes.CreateOneAsync(usersIdx, cancellationToken: ct);

        var bUserIdx = new CreateIndexModel<Birthday>(
            Builders<Birthday>.IndexKeys.Ascending(b => b.UserId),
            new CreateIndexOptions { Name = "ix_birthdays_user" });
            await Birthdays.Indexes.CreateOneAsync(bUserIdx, cancellationToken: ct);

        var bUniqueName = new CreateIndexModel<Birthday>(
            Builders<Birthday>.IndexKeys.Ascending(b => b.UserId).Ascending(b => b.Name),
            new CreateIndexOptions { Name = "ux_birthdays_user_name", Unique = false }
        );
            await Birthdays.Indexes.CreateOneAsync(bUniqueName, cancellationToken: ct);

        var logsIdx = new CreateIndexModel<DeliveryLog>(
            Builders<DeliveryLog>.IndexKeys.Ascending(l => l.UserId),
            new CreateIndexOptions { Name = "ix_logs_user" });
            await DeliveryLogs.Indexes.CreateOneAsync(logsIdx, cancellationToken: ct);

            _indexesEnsured = true;
            _logger.LogInformation("MongoDB indexes are ensured.");
        }
        finally
        {
            _indexesLock.Release();
        }
    }
}