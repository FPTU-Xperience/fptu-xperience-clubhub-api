using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Backend.StabilizationTests;

public sealed class RedisStreamRetentionTests
{
    [Fact]
    public void RedisStreamOptions_DefaultValues_EnforceBoundedRetention()
    {
        var options = new RedisStreamOptions();

        Assert.Equal("clubreporthub-events", options.StreamName);
        Assert.Equal("clubreporthub-events-dlq", options.DeadLetterStreamName);
        Assert.Equal(10000, options.MaxStreamLength);
        Assert.Equal(5000, options.MaxDeadLetterStreamLength);
        Assert.True(options.UseApproximateTrimming);
        Assert.Equal(60, options.KeepAliveSeconds);
        Assert.Equal(5000, options.ConnectTimeoutMs);
        Assert.Equal(5000, options.SyncTimeoutMs);
    }

    [Fact]
    public void RedisStreamOptions_BindsFromConfigurationCorrectly()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Redis:ConnectionString"] = "redis-cluster:6379",
            ["Redis:StreamName"] = "custom-events",
            ["Redis:DeadLetterStreamName"] = "custom-dlq",
            ["Redis:MaxStreamLength"] = "25000",
            ["Redis:MaxDeadLetterStreamLength"] = "12000",
            ["Redis:UseApproximateTrimming"] = "false",
            ["Redis:KeepAliveSeconds"] = "120",
            ["Redis:ConnectTimeoutMs"] = "8000",
            ["Redis:SyncTimeoutMs"] = "9000"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var options = new RedisStreamOptions();
        configuration.GetSection(RedisStreamOptions.SectionName).Bind(options);

        Assert.Equal("redis-cluster:6379", options.ConnectionString);
        Assert.Equal("custom-events", options.StreamName);
        Assert.Equal("custom-dlq", options.DeadLetterStreamName);
        Assert.Equal(25000, options.MaxStreamLength);
        Assert.Equal(12000, options.MaxDeadLetterStreamLength);
        Assert.False(options.UseApproximateTrimming);
        Assert.Equal(120, options.KeepAliveSeconds);
        Assert.Equal(8000, options.ConnectTimeoutMs);
        Assert.Equal(9000, options.SyncTimeoutMs);
    }

    [Fact]
    public async Task PublishAsync_AppliesDefaultStreamTrimmingParameters()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var options = Options.Create(new RedisStreamOptions
        {
            StreamName = "clubreporthub-events",
            MaxStreamLength = 10000,
            UseApproximateTrimming = true
        });

        database.StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult<RedisValue>("1700000000000-0"));

        var eventBus = new RedisStreamEventBus(
            multiplexer,
            options,
            NullLogger<RedisStreamEventBus>.Instance);

        var @event = new ClubCreatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            42,
            "FPTU_JS",
            "Japanese Society");

        await eventBus.PublishAsync(@event, "club.created");

        await database.Received(1).StreamAddAsync(
            "clubreporthub-events",
            Arg.Is<NameValueEntry[]>(entries =>
                entries.Any(e => e.Name == "eventId" && e.Value == @event.EventId.ToString()) &&
                entries.Any(e => e.Name == "eventType" && e.Value == "club.created") &&
                entries.Any(e => e.Name == "clubId" && e.Value == "42") &&
                entries.Any(e => e.Name == "schemaVersion" && e.Value == "1.0")),
            messageId: null,
            maxLength: 10000,
            useApproximateMaxLength: true,
            flags: CommandFlags.None);
    }

    [Theory]
    [InlineData(50000, true)]
    [InlineData(1000, false)]
    public async Task PublishAsync_RespectsConfiguredMaxStreamLengthAndTrimming(int maxStreamLength, bool useApproximate)
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var options = Options.Create(new RedisStreamOptions
        {
            StreamName = "events-stream",
            MaxStreamLength = maxStreamLength,
            UseApproximateTrimming = useApproximate
        });

        database.StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult<RedisValue>("1700000000000-0"));

        var eventBus = new RedisStreamEventBus(
            multiplexer,
            options,
            NullLogger<RedisStreamEventBus>.Instance);

        var @event = new UserRegisteredEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            101,
            "student@fpt.edu.vn",
            "Nguyen Van A");

        await eventBus.PublishAsync(@event, "user.registered");

        await database.Received(1).StreamAddAsync(
            "events-stream",
            Arg.Any<NameValueEntry[]>(),
            messageId: null,
            maxLength: maxStreamLength,
            useApproximateMaxLength: useApproximate,
            flags: CommandFlags.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PublishAsync_WhenMaxStreamLengthIsZeroOrNegative_OmitsTrimming(int disabledLength)
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var options = Options.Create(new RedisStreamOptions
        {
            StreamName = "unbounded-stream",
            MaxStreamLength = disabledLength
        });

        database.StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult<RedisValue>("1700000000000-0"));

        var eventBus = new RedisStreamEventBus(
            multiplexer,
            options,
            NullLogger<RedisStreamEventBus>.Instance);

        var @event = new ClubCreatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            1,
            "CLB",
            "CLB Name");

        await eventBus.PublishAsync(@event, "club.created");

        await database.Received(1).StreamAddAsync(
            "unbounded-stream",
            Arg.Any<NameValueEntry[]>(),
            messageId: null,
            maxLength: null,
            useApproximateMaxLength: Arg.Any<bool>(),
            flags: CommandFlags.None);
    }

    [Fact]
    public async Task PublishAsync_IncludesCorrelationIdFromHttpContext()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var options = Options.Create(new RedisStreamOptions());

        database.StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>())
            .Returns(Task.FromResult<RedisValue>("1700000000000-0"));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-ID"] = "corr-test-12345";
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(httpContext);

        var eventBus = new RedisStreamEventBus(
            multiplexer,
            options,
            NullLogger<RedisStreamEventBus>.Instance,
            httpContextAccessor);

        var @event = new ClubCreatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            5,
            "TEST",
            "Test Club");

        await eventBus.PublishAsync(@event, "club.created");

        await database.Received(1).StreamAddAsync(
            options.Value.StreamName,
            Arg.Is<NameValueEntry[]>(entries =>
                entries.Any(e => e.Name == "correlationId" && e.Value == "corr-test-12345")),
            messageId: null,
            maxLength: 10000,
            useApproximateMaxLength: true,
            flags: CommandFlags.None);
    }

    [Fact]
    public async Task PublishAsync_RetriesOnRedisExceptionUntilExhausted()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var options = Options.Create(new RedisStreamOptions
        {
            MaxRetries = 2,
            RetryBaseDelayMs = 1 // fast for tests
        });

        database.StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated network failure"));

        var eventBus = new RedisStreamEventBus(
            multiplexer,
            options,
            NullLogger<RedisStreamEventBus>.Instance);

        var @event = new ClubCreatedEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            1,
            "CODE",
            "Name");

        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            eventBus.PublishAsync(@event, "test.retry"));

        // MaxRetries = 2 means attempt 1, retry attempt 2, then throw
        await database.Received(2).StreamAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public void DockerCompose_RedisConfiguration_HasAdequateMemoryAndNoeviction()
    {
        var root = FindRepositoryRoot();
        var composeContent = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));

        // Redis command must configure maxmemory at least 256mb and noeviction policy
        Assert.Contains("--maxmemory 256mb", composeContent);
        Assert.Contains("--maxmemory-policy noeviction", composeContent);

        // Deploy memory limit must be at least 384M
        Assert.Contains("memory: 384M", composeContent);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClubReportHub.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate ClubReportHub.sln.");
    }
}
