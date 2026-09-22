using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ClubReportHub.Shared.Messaging;

public static class RedisStreamServiceCollectionExtensions
{
    /// <summary>
    /// Registers Redis Streams infrastructure: IConnectionMultiplexer as singleton,
    /// RedisStreamOptions from configuration, and RedisStreamEventBus as IEventBus.
    /// </summary>
    public static IServiceCollection AddRedisStreamEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisStreamOptions>(configuration.GetSection(RedisStreamOptions.SectionName));
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisStreamOptions>>().Value;
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.AbortOnConnectFail = false;
            config.ConnectRetry = options.MaxRetries > 0 ? options.MaxRetries : 3;
            config.ConnectTimeout = options.ConnectTimeoutMs;
            config.SyncTimeout = options.SyncTimeoutMs;
            config.KeepAlive = options.KeepAliveSeconds;
            config.ClientName = "ClubReportHub";
            return ConnectionMultiplexer.Connect(config);
        });
        services.AddHttpContextAccessor();
        services.AddSingleton<IEventBus, RedisStreamEventBus>();
        return services;
    }
}
