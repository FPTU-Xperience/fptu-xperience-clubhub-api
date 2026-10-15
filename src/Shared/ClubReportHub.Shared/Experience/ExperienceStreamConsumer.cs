using System.Text.Json;
using ClubReportHub.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClubReportHub.Shared.Experience;

public interface IExperienceEventHandler
{
    bool Handles(string route);
    Task ProcessAsync(Guid eventId, string route, JsonElement payload, CancellationToken cancellationToken);
}

// Each service has its own group and transactional inbox. Acknowledgement follows
// the database commit; failed messages remain pending and can be reclaimed.
public sealed class ExperienceStreamConsumer<THandler>(IConnectionMultiplexer redis,
    IServiceScopeFactory scopes, IOptions<RedisStreamOptions> settings,
    ILogger<ExperienceStreamConsumer<THandler>> logger) : BackgroundService where THandler : class, IExperienceEventHandler
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        var group = "experience-" + typeof(THandler).FullName;
        var consumer = Environment.MachineName + "-" + Guid.NewGuid().ToString("N");
        var database = redis.GetDatabase();
        var recoveryCursor = (RedisValue)"0-0";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                try { await database.StreamCreateConsumerGroupAsync(options.StreamName, group, "0-0", createStream: true); }
                catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP", StringComparison.Ordinal)) { }
                var claimed = await database.StreamAutoClaimAsync(options.StreamName, group, consumer,
                    Math.Max(options.PendingMessageIdleThresholdMs, 1000), recoveryCursor, count: options.BatchSize);
                recoveryCursor = claimed.NextStartId;
                foreach (var entry in claimed.ClaimedEntries) await ProcessAsync(entry);
                var entries = await database.StreamReadGroupAsync(options.StreamName, group, consumer, ">", count: options.BatchSize);
                foreach (var entry in entries) await ProcessAsync(entry);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Experience stream processing failed for {Group}; event remains pending", group); }
            await Task.Delay(Math.Max(options.PollIntervalMs, 100), stoppingToken);
        }

        async Task ProcessAsync(StreamEntry entry)
        {
            var route = entry.Values.FirstOrDefault(x => x.Name == "eventType").Value.ToString();
            using var scope = scopes.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            if (handler.Handles(route))
            {
                var id = Guid.Parse(entry.Values.First(x => x.Name == "eventId").Value.ToString());
                using var document = JsonDocument.Parse(entry.Values.First(x => x.Name == "payload").Value.ToString());
                await handler.ProcessAsync(id, route, document.RootElement, stoppingToken);
            }
            await database.StreamAcknowledgeAsync(options.StreamName, group, entry.Id);
        }
    }
}
