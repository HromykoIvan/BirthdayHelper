using BirthdayBot.Infrastructure.Mongo;
using MongoDB.Driver;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BirthdayBot.Infrastructure.Services;

/// <summary>
/// Ensures MongoDB indexes in background with retries.
/// This avoids crashing the app on transient Mongo connectivity issues at startup.
/// </summary>
public sealed class MongoIndexInitializerHostedService : BackgroundService
{
    private readonly MongoContext _mongoContext;
    private readonly ILogger<MongoIndexInitializerHostedService> _logger;

    public MongoIndexInitializerHostedService(
        MongoContext mongoContext,
        ILogger<MongoIndexInitializerHostedService> logger)
    {
        _mongoContext = mongoContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;
        var delay = TimeSpan.FromSeconds(5);

        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await _mongoContext.EnsureIndexesAsync(stoppingToken);
                _logger.LogInformation("MongoDB indexes ensured on attempt {Attempt}.", attempt);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                _logger.LogWarning(ex,
                    "Mongo index initialization failed on attempt {Attempt}. Retrying in {DelaySeconds}s.",
                    attempt, delay.TotalSeconds);

                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }
}
