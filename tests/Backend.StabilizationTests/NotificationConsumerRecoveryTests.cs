using System.Reflection;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.Models;
using NSubstitute;
using StackExchange.Redis;

namespace Backend.StabilizationTests;

public sealed class NotificationConsumerRecoveryTests : IAsyncDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly DbContextOptions<NotificationDbContext> _dbOptions;

    public NotificationConsumerRecoveryTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
        _dbOptions = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        using var db = new NotificationDbContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _sqliteConnection.DisposeAsync();
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new NotificationDbContext(_dbOptions));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static StreamPendingMessageInfo CreatePendingInfo(
        RedisValue messageId,
        RedisValue consumerName,
        long idleTimeInMilliseconds,
        long deliveryCount)
    {
        var ctors = typeof(StreamPendingMessageInfo).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var ctor in ctors)
        {
            var p = ctor.GetParameters();
            if (p.Length == 4)
            {
                var rawArgs = new object[] { messageId, consumerName, idleTimeInMilliseconds, deliveryCount };
                var args = new object[4];
                for (var i = 0; i < 4; i++)
                {
                    if (p[i].ParameterType == typeof(int) && rawArgs[i] is long l)
                        args[i] = (int)l;
                    else if (p[i].ParameterType == typeof(long) && rawArgs[i] is int val)
                        args[i] = (long)val;
                    else
                        args[i] = rawArgs[i];
                }
                return (StreamPendingMessageInfo)ctor.Invoke(args);
            }
        }

        var obj = (StreamPendingMessageInfo)Activator.CreateInstance(typeof(StreamPendingMessageInfo))!;
        object boxed = obj;
        var fields = typeof(StreamPendingMessageInfo).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.Name.Contains("messageId", StringComparison.OrdinalIgnoreCase)) field.SetValue(boxed, messageId);
            else if (field.Name.Contains("consumer", StringComparison.OrdinalIgnoreCase)) field.SetValue(boxed, consumerName);
            else if (field.Name.Contains("idle", StringComparison.OrdinalIgnoreCase)) field.SetValue(boxed, field.FieldType == typeof(int) ? (int)idleTimeInMilliseconds : idleTimeInMilliseconds);
            else if (field.Name.Contains("delivery", StringComparison.OrdinalIgnoreCase)) field.SetValue(boxed, field.FieldType == typeof(int) ? (int)deliveryCount : deliveryCount);
        }
        return (StreamPendingMessageInfo)boxed;
    }

    [Fact]
    public void RedisStreamOptions_DefaultPendingRecoverySettings_AreSensible()
    {
        var options = new RedisStreamOptions();

        Assert.Equal(5000, options.PendingRecoveryIntervalMs);
        Assert.Equal(10000, options.PendingMessageIdleThresholdMs);
        Assert.Equal(3, options.MaxDeliveryAttempts);
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenEventAlreadyProcessed_SkipsAndAcknowledgesImmediately()
    {
        var scopeFactory = CreateScopeFactory();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var eventId = Guid.NewGuid();
        using (var db = new NotificationDbContext(_dbOptions))
        {
            db.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = eventId,
                RoutingKey = "club.created",
                ProcessedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var consumer = new RedisStreamNotificationConsumer(
            scopeFactory,
            multiplexer,
            Options.Create(new RedisStreamOptions()),
            NullLogger<RedisStreamNotificationConsumer>.Instance);

        var entry = new StreamEntry("1700000000000-0",
        [
            new("eventId", eventId.ToString()),
            new("eventType", "club.created"),
            new("payload", "{}")
        ]);

        await consumer.ProcessMessageAsync(entry, CancellationToken.None);

        // Acknowledge should be called because it was already processed
        await database.Received(1).StreamAcknowledgeAsync(
            "clubreporthub-events",
            "notification-service",
            entry.Id);

        // No new notifications should have been added
        using (var db = new NotificationDbContext(_dbOptions))
        {
            Assert.Empty(db.Notifications);
        }
    }

    [Fact]
    public async Task ProcessMessageAsync_OnValidEvent_CreatesNotificationAndAcknowledges()
    {
        var scopeFactory = CreateScopeFactory();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var consumer = new RedisStreamNotificationConsumer(
            scopeFactory,
            multiplexer,
            Options.Create(new RedisStreamOptions()),
            NullLogger<RedisStreamNotificationConsumer>.Instance);

        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            clubName = "FPT Guitar Club",
            title = "Acoustic Night",
            recipientUserIds = new[] { 101, 102 }
        });

        var entry = new StreamEntry("1700000000001-0",
        [
            new("eventId", eventId.ToString()),
            new("eventType", EventRoutingKeys.ActivityCreated),
            new("payload", payload)
        ]);

        await consumer.ProcessMessageAsync(entry, CancellationToken.None);

        await database.Received(1).StreamAcknowledgeAsync(
            "clubreporthub-events",
            "notification-service",
            entry.Id);

        using var db = new NotificationDbContext(_dbOptions);
        var notifications = await db.Notifications.ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.RecipientUserId == 101);
        Assert.Contains(notifications, n => n.RecipientUserId == 102);

        var processed = await db.ProcessedEvents.FirstOrDefaultAsync(x => x.EventId == eventId);
        Assert.NotNull(processed);
        Assert.Equal(EventRoutingKeys.ActivityCreated, processed.RoutingKey);
    }

    [Fact]
    public async Task ProcessMessageAsync_OnFailureUnderMaxRetries_LeavesMessagePending()
    {
        var scopeFactory = CreateScopeFactory();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var entryId = new RedisValue("1700000000002-0");
        // Mock pending messages returning deliveryCount = 1 (under max 3)
        var pendingInfo = CreatePendingInfo(
            entryId,
            "test-consumer",
            idleTimeInMilliseconds: 2000,
            deliveryCount: 1);

        database.StreamPendingMessagesAsync(
            "clubreporthub-events",
            "notification-service",
            1,
            RedisValue.Null,
            entryId,
            entryId)
            .Returns([pendingInfo]);

        var consumer = new RedisStreamNotificationConsumer(
            scopeFactory,
            multiplexer,
            Options.Create(new RedisStreamOptions { MaxDeliveryAttempts = 3 }),
            NullLogger<RedisStreamNotificationConsumer>.Instance);

        // Invalid json payload will throw an exception during parsing
        var entry = new StreamEntry(entryId,
        [
            new("eventId", Guid.NewGuid().ToString()),
            new("eventType", EventRoutingKeys.ActivityCreated),
            new("payload", "{ INVALID_JSON }")
        ]);

        await consumer.ProcessMessageAsync(entry, CancellationToken.None);

        // Should NOT acknowledge because attempt 1 is under max 3
        await database.DidNotReceive().StreamAcknowledgeAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<RedisValue>());

        // Should NOT write to DLQ yet
        await database.DidNotReceive().StreamAddAsync(
            "clubreporthub-events-dlq",
            Arg.Any<NameValueEntry[]>(),
            Arg.Any<RedisValue?>(),
            Arg.Any<int?>(),
            Arg.Any<bool>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenDeliveryAttemptsExhausted_MovesToDlqAndAcknowledges()
    {
        var scopeFactory = CreateScopeFactory();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var entryId = new RedisValue("1700000000003-0");
        var eventId = Guid.NewGuid();

        // Mock pending messages returning deliveryCount = 3 (reached max 3)
        var pendingInfo = CreatePendingInfo(
            entryId,
            "test-consumer",
            idleTimeInMilliseconds: 35000,
            deliveryCount: 3);

        database.StreamPendingMessagesAsync(
            "clubreporthub-events",
            "notification-service",
            1,
            RedisValue.Null,
            entryId,
            entryId)
            .Returns([pendingInfo]);

        var consumer = new RedisStreamNotificationConsumer(
            scopeFactory,
            multiplexer,
            Options.Create(new RedisStreamOptions { MaxDeliveryAttempts = 3 }),
            NullLogger<RedisStreamNotificationConsumer>.Instance);

        var entry = new StreamEntry(entryId,
        [
            new("eventId", eventId.ToString()),
            new("eventType", EventRoutingKeys.ActivityCreated),
            new("payload", "{ MALFORMED_JSON_TEST }")
        ]);

        await consumer.ProcessMessageAsync(entry, CancellationToken.None);

        // Must write to DLQ
        await database.Received(1).StreamAddAsync(
            "clubreporthub-events-dlq",
            Arg.Is<NameValueEntry[]>(vals =>
                vals.Any(v => v.Name == "errorReason") &&
                vals.Any(v => v.Name == "deliveryCount" && v.Value == "3") &&
                vals.Any(v => v.Name == "originalStream" && v.Value == "clubreporthub-events")),
            maxLength: 5000,
            useApproximateMaxLength: true,
            flags: CommandFlags.None);

        // Must acknowledge from main stream to purge from PEL
        await database.Received(1).StreamAcknowledgeAsync(
            "clubreporthub-events",
            "notification-service",
            entryId);

        // Must record in ProcessedEvents with DLQ tag
        using var db = new NotificationDbContext(_dbOptions);
        var processed = await db.ProcessedEvents.FirstOrDefaultAsync(x => x.EventId == eventId);
        Assert.NotNull(processed);
        Assert.Equal($"{EventRoutingKeys.ActivityCreated}:DLQ", processed.RoutingKey);
    }

    [Fact]
    public async Task CatchUpExistingMessagesAsync_PaginatesUntilNoMoreUnacknowledged()
    {
        var scopeFactory = CreateScopeFactory();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var entry1 = new StreamEntry("1700000000010-0",
        [
            new("eventId", Guid.NewGuid().ToString()),
            new("eventType", "dummy.event"),
            new("payload", "{}")
        ]);

        // First call with "0-0" returns entry1
        database.StreamReadGroupAsync(
            "clubreporthub-events",
            "notification-service",
            Arg.Any<RedisValue>(),
            (RedisValue)"0-0",
            count: 5)
            .Returns([entry1]);

        // Second call with lastId "1700000000010-0" returns empty array (finished)
        database.StreamReadGroupAsync(
            "clubreporthub-events",
            "notification-service",
            Arg.Any<RedisValue>(),
            (RedisValue)"1700000000010-0",
            count: 5)
            .Returns(Array.Empty<StreamEntry>());

        var consumer = new RedisStreamNotificationConsumer(
            scopeFactory,
            multiplexer,
            Options.Create(new RedisStreamOptions { BatchSize = 5 }),
            NullLogger<RedisStreamNotificationConsumer>.Instance);

        await consumer.CatchUpExistingMessagesAsync(CancellationToken.None);

        // Verifies second page was requested using the previous entry's ID
        await database.Received(1).StreamReadGroupAsync(
            "clubreporthub-events",
            "notification-service",
            Arg.Any<RedisValue>(),
            (RedisValue)"1700000000010-0",
            count: 5);
    }

    [Fact]
    public async Task RecoverPendingMessagesAsync_ClaimsAndProcessesStaleMessages()
    {
        var scopeFactory = CreateScopeFactory();
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);

        var staleEntryId = new RedisValue("1700000000020-0");
        var freshEntryId = new RedisValue("1700000000021-0");

        // Stale message: idle for 15,000ms (threshold is 10,000ms)
        var staleInfo = CreatePendingInfo(
            staleEntryId,
            "crashed-consumer",
            idleTimeInMilliseconds: 15000,
            deliveryCount: 1);

        // Fresh message: idle for 1,000ms (not yet eligible for recovery)
        var freshInfo = CreatePendingInfo(
            freshEntryId,
            "active-consumer",
            idleTimeInMilliseconds: 1000,
            deliveryCount: 1);

        database.StreamPendingMessagesAsync(
            "clubreporthub-events",
            "notification-service",
            count: 5,
            consumerName: RedisValue.Null,
            minId: "-",
            maxId: "+")
            .Returns([staleInfo, freshInfo]);

        var claimedEntry = new StreamEntry(staleEntryId,
        [
            new("eventId", Guid.NewGuid().ToString()),
            new("eventType", "recovered.event"),
            new("payload", "{}")
        ]);

        database.StreamClaimAsync(
            "clubreporthub-events",
            "notification-service",
            Arg.Any<RedisValue>(),
            minIdleTimeInMs: 10000,
            messageIds: Arg.Is<RedisValue[]>(ids => ids.Length == 1 && ids[0] == staleEntryId))
            .Returns([claimedEntry]);

        var consumer = new RedisStreamNotificationConsumer(
            scopeFactory,
            multiplexer,
            Options.Create(new RedisStreamOptions
            {
                PendingMessageIdleThresholdMs = 10000,
                BatchSize = 5
            }),
            NullLogger<RedisStreamNotificationConsumer>.Instance);

        await consumer.RecoverPendingMessagesAsync(CancellationToken.None);

        // Only the stale message should have been claimed
        await database.Received(1).StreamClaimAsync(
            "clubreporthub-events",
            "notification-service",
            Arg.Any<RedisValue>(),
            minIdleTimeInMs: 10000,
            messageIds: Arg.Is<RedisValue[]>(ids => ids.Length == 1 && ids[0] == staleEntryId));

        // The claimed message should then be processed and acknowledged
        await database.Received(1).StreamAcknowledgeAsync(
            "clubreporthub-events",
            "notification-service",
            staleEntryId);
    }
}
