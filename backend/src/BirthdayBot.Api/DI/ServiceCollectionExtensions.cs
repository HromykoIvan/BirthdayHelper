
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

namespace BirthdayBot.Api.DI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBotServices(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<BotOptions>(cfg.GetSection("Bot"));
        services.Configure<SchedulerOptions>(cfg.GetSection("Scheduler"));
        services.Configure<MongoOptions>(cfg.GetSection("Mongo"));
        services.Configure<ReminderOptions>(cfg.GetSection("Reminder"));
        services.Configure<MetricsOptions>(cfg.GetSection("Metrics"));
        services.Configure<AiEvalOptions>(cfg.GetSection("AiEval"));
        services.Configure<LocalAiOptions>(cfg.GetSection("LocalAi"));
        services.Configure<OpenAiOptions>(cfg.GetSection("OpenAi"));
        services.Configure<VkImportOptions>(cfg.GetSection("VkImport"));
        services.Configure<UserRateLimitOptions>(cfg.GetSection("UserRateLimit"));
        services.Configure<PromptProfileOptions>(cfg.GetSection("PromptProfiles"));

        services.AddSingleton<MongoContext>();

        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IBirthdayRepository, BirthdayRepository>();
        services.AddSingleton<IDeliveryLogRepository, DeliveryLogRepository>();
        services.AddSingleton<IAiEventRepository, AiEventRepository>();

        services.AddSingleton<IGreetingGenerator, GreetingGenerator>();
        services.AddSingleton<AiMetrics>();
        services.AddSingleton<LocalIntentRouter>();
        services.AddSingleton<IIntentRouter, OpenAiIntentRouter>();
        services.AddSingleton<IUserUpdateRateLimiter, UserUpdateRateLimiter>();
        services.AddSingleton<IAiGreetingEnhancer, OpenAiGreetingEnhancer>();

        services.AddHttpClient<OpenAiResponsesClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 3, 60));
        });
        services.AddSingleton<IAiEvalService, AiEvalService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.AddSingleton<InMemoryConversationState>();
        services.AddScoped<IUpdateHandler, UpdateHandler>();
        services.AddScoped<TelegramBirthdayImportService>();
        services.AddScoped<PersonProfileService>();
        services.AddHttpClient<VkImportService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddMemoryCache();
        services.AddSingleton<IConversationSessionStore, MongoConversationSessionStore>();
        services.AddSingleton<IAiFeedbackSessionStore, MongoAiFeedbackSessionStore>();
        services.AddScoped<IWizardFlow, AddBirthdayWizardFlow>();
        // TimeZoneResolver + HttpClient
        services.AddHttpClient<ITimeZoneResolver, TimeZoneResolver>();

        // Upcoming
        services.AddSingleton<IUpcomingService, UpcomingService>();

        // Flow - регистрируем как Scoped для UpdateHandler
        services.AddScoped<AddBirthdayWizardFlow>();
        
        // Register Telegram Bot Client - use mock in Development mode
        var environment = cfg["ASPNETCORE_ENVIRONMENT"] ?? "Production";
        var useMock = environment == "Development" && cfg.GetValue<bool>("Bot:UseMockClient", false);
        
        if (useMock)
        {
            services.AddSingleton<MockTelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(sp => 
            {
                var logger = sp.GetRequiredService<ILogger<MockTelegramBotClient>>();
                return new MockTelegramBotClientAdapter(logger);
            });
        }
        else
        {
            services.AddSingleton<ITelegramBotClient>(sp =>
            {
                var bot = sp.GetRequiredService<IOptions<BotOptions>>().Value;
                return new TelegramBotClient(bot.Token);
            });
        }

        services.AddHostedService<MongoIndexInitializerHostedService>();

        services.AddSingleton<IReminderService, ReminderService>();
        var reminderOptions = cfg.GetSection("Reminder").Get<ReminderOptions>() ?? new ReminderOptions();
        if (reminderOptions.RunAsHostedService)
        {
            services.AddHostedService<ReminderHostedService>();
        }

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
                    builder.AddMeter(AiMetrics.MeterName);
                    builder.AddPrometheusExporter();
                });
        }

        return services;
    }
}
