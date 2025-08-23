
using BirthdayBot.Api.Options;
using BirthdayBot.Application.Interfaces;
using BirthdayBot.Infrastructure.Mongo;
using BirthdayBot.Infrastructure.Options;
using BirthdayBot.Infrastructure.Services;
using BirthdayBot.Infrastructure.State;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using Telegram.Bot;
using BirthdayBot.Application.Services;
using BirthdayBot.Infrastructure.Sessions;
using BirthdayBot.Infrastructure.Geo;
using Microsoft.Extensions.Caching.Memory;
using BirthdayBot.Infrastructure.Flows;

namespace BirthdayBot.Api.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBotServices(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<BotOptions>(cfg.GetSection("Bot"));
        services.Configure<MongoOptions>(cfg.GetSection("Mongo"));
        services.Configure<ReminderOptions>(cfg.GetSection("Reminder"));
        services.Configure<MetricsOptions>(cfg.GetSection("Metrics"));

        services.AddSingleton<MongoContext>();

        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IBirthdayRepository, BirthdayRepository>();
        services.AddSingleton<IDeliveryLogRepository, DeliveryLogRepository>();

        services.AddSingleton<IGreetingGenerator, GreetingGenerator>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.AddSingleton<InMemoryConversationState>();
        services.AddScoped<IUpdateHandler, UpdateHandler>();
        services.AddMemoryCache();
        services.AddSingleton<IConversationSessionStore, InMemoryConversationSessionStore>();
        services.AddScoped<IWizardFlow, AddBirthdayWizardFlow>();
        // Хранилище сессий мастера
        services.AddSingleton<IAddBirthdayWizardSessionStore, InMemoryAddBirthdayWizardSessionStore>();

        // TimeZoneResolver + HttpClient
        services.AddHttpClient<ITimeZoneResolver, TimeZoneResolver>();

        // Upcoming
        services.AddSingleton<IUpcomingService, UpcomingService>();

        // Flow - регистрируем как Scoped для UpdateHandler
        services.AddScoped<AddBirthdayWizardFlow>();
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var bot = sp.GetRequiredService<IOptions<BotOptions>>().Value;
            return new TelegramBotClient(bot.Token);
        });

        services.AddHostedService<ReminderHostedService>();

        services.AddHealthChecks()
            .AddMongoDb(sp => sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString, name: "mongodb");

        var metrics = cfg.GetSection("Metrics").Get<MetricsOptions>() ?? new MetricsOptions();
        if (metrics.Enable)
        {
            services.AddOpenTelemetry()
                .WithMetrics(builder =>
                {
                    builder.AddAspNetCoreInstrumentation();
                    builder.AddRuntimeInstrumentation();
                    builder.AddPrometheusExporter();
                });
        }

        return services;
    }
}