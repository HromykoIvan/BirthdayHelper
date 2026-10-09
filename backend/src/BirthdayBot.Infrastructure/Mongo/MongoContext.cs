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
    public IMongoCollection<AiEvent> AiEvents => Database.GetCollection<AiEvent>("ai_events");
    public IMongoCollection<ConversationSessionDocument> ConversationSessions =>
        Database.GetCollection<ConversationSessionDocument>("conversation_sessions");
    public IMongoCollection<AiFeedbackSessionDocument> AiFeedbackSessions =>
        Database.GetCollection<AiFeedbackSessionDocument>("ai_feedback_sessions");
    public IMongoCollection<BirthdayImportSessionDocument> ImportSessions =>
        Database.GetCollection<BirthdayImportSessionDocument>("birthday_import_sessions");
    public IMongoCollection<PersonProfileSessionDocument> PersonProfileSessions =>
        Database.GetCollection<PersonProfileSessionDocument>("person_profile_sessions");
    public IMongoCollection<VkAuthorizationSessionDocument> VkAuthorizationSessions =>
        Database.GetCollection<VkAuthorizationSessionDocument>("vk_authorization_sessions");

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
            BsonClassMap.RegisterClassMap<AiEvent>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(x => x.Id).SetIdGenerator(ObjectIdGenerator.Instance);
                cm.GetMemberMap(x => x.CreatedAtUtc).SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
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

            var deliveryKeyIdx = new CreateIndexModel<DeliveryLog>(
                Builders<DeliveryLog>.IndexKeys.Ascending(l => l.DeliveryKey),
                new CreateIndexOptions
                {
                    Name = "ux_delivery_key",
                    Unique = true,
                    Sparse = true
                });
            await DeliveryLogs.Indexes.CreateOneAsync(deliveryKeyIdx, cancellationToken: ct);

            var aiCreatedIdx = new CreateIndexModel<AiEvent>(
                Builders<AiEvent>.IndexKeys.Descending(e => e.CreatedAtUtc),
                new CreateIndexOptions { Name = "ix_ai_events_created" });
            await AiEvents.Indexes.CreateOneAsync(aiCreatedIdx, cancellationToken: ct);

            var aiIntentIdx = new CreateIndexModel<AiEvent>(
                Builders<AiEvent>.IndexKeys.Ascending(e => e.EventType).Ascending(e => e.ExpectedIntent),
                new CreateIndexOptions { Name = "ix_ai_events_intent_eval" });
            await AiEvents.Indexes.CreateOneAsync(aiIntentIdx, cancellationToken: ct);

            var conversationTtlIdx = new CreateIndexModel<ConversationSessionDocument>(
                Builders<ConversationSessionDocument>.IndexKeys.Ascending(x => x.ExpiresAtUtc),
                new CreateIndexOptions
                {
                    Name = "ttl_conversation_sessions",
                    ExpireAfter = TimeSpan.Zero
                });
            await ConversationSessions.Indexes.CreateOneAsync(conversationTtlIdx, cancellationToken: ct);

            var feedbackTtlIdx = new CreateIndexModel<AiFeedbackSessionDocument>(
                Builders<AiFeedbackSessionDocument>.IndexKeys.Ascending(x => x.ExpiresAtUtc),
                new CreateIndexOptions
                {
                    Name = "ttl_ai_feedback_sessions",
                    ExpireAfter = TimeSpan.Zero
                });
            await AiFeedbackSessions.Indexes.CreateOneAsync(feedbackTtlIdx, cancellationToken: ct);

            var importSessionTtl = new CreateIndexModel<BirthdayImportSessionDocument>(
                Builders<BirthdayImportSessionDocument>.IndexKeys.Ascending(x => x.ExpiresAtUtc),
                new CreateIndexOptions { Name = "ttl_birthday_import_sessions", ExpireAfter = TimeSpan.Zero });
            await ImportSessions.Indexes.CreateOneAsync(importSessionTtl, cancellationToken: ct);

            var vkAuthTtl = new CreateIndexModel<VkAuthorizationSessionDocument>(
                Builders<VkAuthorizationSessionDocument>.IndexKeys.Ascending(x => x.ExpiresAtUtc),
                new CreateIndexOptions { Name = "ttl_vk_authorization_sessions", ExpireAfter = TimeSpan.Zero });
            await VkAuthorizationSessions.Indexes.CreateOneAsync(vkAuthTtl, cancellationToken: ct);

            var peopleSessionTtl = new CreateIndexModel<PersonProfileSessionDocument>(
                Builders<PersonProfileSessionDocument>.IndexKeys.Ascending(x => x.ExpiresAtUtc),
                new CreateIndexOptions { Name = "ttl_person_profile_sessions", ExpireAfter = TimeSpan.Zero });
            await PersonProfileSessions.Indexes.CreateOneAsync(peopleSessionTtl, cancellationToken: ct);

            _indexesEnsured = true;
            _logger.LogInformation("MongoDB indexes are ensured.");
        }
        finally
        {
            _indexesLock.Release();
        }
    }
}