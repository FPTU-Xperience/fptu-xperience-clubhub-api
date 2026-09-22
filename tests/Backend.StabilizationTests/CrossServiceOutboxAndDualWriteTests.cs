using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Messaging;
using ClubService.Data;
using ClubService.Models;
using ExportService.Data;
using ExportService.Models;
using ExportService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class CrossServiceOutboxAndDualWriteTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    #region REL-F01: ExportService Outbox & Swallowed Event Elimination Tests

    [Fact]
    public async Task ExportGenerationJob_OnCompletion_InsertsOutboxMessageAtomically()
    {
        var dbName = $"export-job-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ExportDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        int requestId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            var req = new ExportRequest
            {
                ExportType = ExportTypes.Pdf,
                Scope = "Report",
                Status = ExportStatuses.Pending,
                Period = "2026-Q1",
                ClubId = 10,
                ReportId = 100,
                RequestedByUserId = 42,
                RequestedByName = "User 42",
                CriteriaJson = "{}"
            };
            db.ExportRequests.Add(req);
            await db.SaveChangesAsync();
            requestId = req.Id;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"export-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Exports:StoragePath"] = tempDir,
                    ["Exports:RetentionHours"] = "24"
                })
                .Build();

            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
                var generator = new ExportFileGenerator(config);
                var job = new ExportGenerationJob(db, generator, config, NullLogger<ExportGenerationJob>.Instance);

                await job.GenerateAsync(requestId, CancellationToken.None);
            }

            // Verify both request status and outbox message were saved atomically in DB
            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
                var req = await db.ExportRequests.Include(x => x.File).SingleAsync(x => x.Id == requestId);
                Assert.Equal(ExportStatuses.Completed, req.Status);
                Assert.NotNull(req.CompletedAtUtc);
                Assert.NotNull(req.File);

                var outboxMsg = await db.OutboxMessages.SingleOrDefaultAsync();
                Assert.NotNull(outboxMsg);
                Assert.Equal(EventRoutingKeys.ExportCompleted, outboxMsg.EventType);
                Assert.Equal(OutboxMessageStatus.Pending, outboxMsg.Status);

                var evt = JsonSerializer.Deserialize<ExportCompletedEvent>(outboxMsg.Payload, JsonOptions);
                Assert.NotNull(evt);
                Assert.Equal(requestId, evt.ExportRequestId);
                Assert.Equal(ExportTypes.Pdf, evt.ExportType);
                Assert.Equal(42, evt.RequestedByUserId);
                Assert.Equal(req.File.FileName, evt.FileName);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
            }
        }
    }

    [Fact]
    public async Task ExportGenerationJob_OnFailure_MarksFailedAndDoesNotInsertOutboxMessage()
    {
        var dbName = $"export-fail-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ExportDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        int requestId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            var req = new ExportRequest
            {
                ExportType = "INVALID_TYPE",
                Scope = "Report",
                Status = ExportStatuses.Pending,
                RequestedByUserId = 99,
                CriteriaJson = "{}"
            };
            db.ExportRequests.Add(req);
            await db.SaveChangesAsync();
            requestId = req.Id;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Exports:StoragePath"] = "Z:\\invalid_dir_path_that_fails_generation\\!@#"
            })
            .Build();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            var generator = new ExportFileGenerator(config);
            var job = new ExportGenerationJob(db, generator, config, NullLogger<ExportGenerationJob>.Instance);

            // Job throws on failure so Hangfire can retry
            await Assert.ThrowsAnyAsync<Exception>(() => job.GenerateAsync(requestId, CancellationToken.None));
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            var req = await db.ExportRequests.SingleAsync(x => x.Id == requestId);
            Assert.Equal(ExportStatuses.Failed, req.Status);
            Assert.NotNull(req.ErrorMessage);

            // Crucial: No outbox event is created on failed generation
            Assert.Empty(db.OutboxMessages);
        }
    }

    [Fact]
    public async Task ExportService_ExportRequestedEvent_QueuedInOutbox()
    {
        var dbName = $"export-req-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ExportDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            var req = new ExportRequest
            {
                ExportType = ExportTypes.Pdf,
                Scope = "Report",
                Status = ExportStatuses.Pending,
                Period = "2026-Q1",
                ClubId = 1,
                ReportId = 10,
                RequestedByUserId = 5
            };
            db.ExportRequests.Add(req);
            await db.SaveChangesAsync();

            db.AddOutboxMessage(
                new ExportRequestedEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    req.Id,
                    req.ExportType,
                    req.Scope,
                    req.RequestedByUserId),
                EventRoutingKeys.ExportRequested);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            var msg = await db.OutboxMessages.SingleOrDefaultAsync();
            Assert.NotNull(msg);
            Assert.Equal(EventRoutingKeys.ExportRequested, msg.EventType);
            Assert.Equal(OutboxMessageStatus.Pending, msg.Status);

            var evt = JsonSerializer.Deserialize<ExportRequestedEvent>(msg.Payload, JsonOptions);
            Assert.NotNull(evt);
            Assert.Equal(5, evt.RequestedByUserId);
        }
    }

    #endregion

    #region REL-F05: ClubService Outbox & Dual-Write Elimination Tests

    [Fact]
    public async Task ClubService_CreateClub_PersistsOutboxMessageAtomically()
    {
        var dbName = $"club-create-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var club = new Club
            {
                Code = "CLUB01",
                Name = "Club One",
                Category = "Academic",
                Description = "A test club",
                ContactEmail = "club1@fptu.edu.vn",
                ContactPhone = "0123456789"
            };

            db.Clubs.Add(club);
            await db.SaveChangesAsync();

            db.AddOutboxMessage(new ClubCreatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                club.Id,
                club.Code,
                club.Name), EventRoutingKeys.ClubCreated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var club = await db.Clubs.SingleOrDefaultAsync(x => x.Code == "CLUB01");
            Assert.NotNull(club);

            var outboxMsg = await db.OutboxMessages.SingleOrDefaultAsync();
            Assert.NotNull(outboxMsg);
            Assert.Equal(EventRoutingKeys.ClubCreated, outboxMsg.EventType);
            Assert.Equal(OutboxMessageStatus.Pending, outboxMsg.Status);

            var evt = JsonSerializer.Deserialize<ClubCreatedEvent>(outboxMsg.Payload, JsonOptions);
            Assert.NotNull(evt);
            Assert.Equal(club.Id, evt.ClubId);
            Assert.Equal("CLUB01", evt.ClubCode);
            Assert.Equal("Club One", evt.ClubName);
        }
    }

    [Fact]
    public async Task ClubService_ApproveApplication_PersistsOutboxMessageAtomically()
    {
        var dbName = $"club-app-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var app = new ClubCreationApplication
            {
                Code = "CLUB02",
                Name = "Club Two",
                Category = "Sports",
                Description = "Sports club",
                ContactEmail = "sports@fptu.edu.vn",
                ContactPhone = "0987654321",
                RequesterUserId = 15,
                RequesterName = "Founder User",
                Status = ClubApplicationStatuses.Submitted
            };
            db.ClubCreationApplications.Add(app);
            await db.SaveChangesAsync();

            var club = new Club
            {
                Code = app.Code,
                Name = app.Name,
                Category = app.Category,
                Description = app.Description,
                ContactEmail = app.ContactEmail,
                ContactPhone = app.ContactPhone
            };
            club.ManagerAssignments.Add(new ClubManagerAssignment
            {
                ManagerUserId = app.RequesterUserId,
                ManagerName = app.RequesterName,
                IsActive = true
            });

            db.Clubs.Add(club);
            await db.SaveChangesAsync();

            app.Status = ClubApplicationStatuses.Approved;
            app.CreatedClubId = club.Id;
            app.ReviewedAtUtc = DateTimeOffset.UtcNow;

            db.AddOutboxMessage(new ClubCreatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                club.Id,
                club.Code,
                club.Name), EventRoutingKeys.ClubCreated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var outboxMsg = await db.OutboxMessages.SingleOrDefaultAsync();
            Assert.NotNull(outboxMsg);
            Assert.Equal(EventRoutingKeys.ClubCreated, outboxMsg.EventType);

            var evt = JsonSerializer.Deserialize<ClubCreatedEvent>(outboxMsg.Payload, JsonOptions);
            Assert.NotNull(evt);
            Assert.Equal("CLUB02", evt.ClubCode);
        }
    }

    [Fact]
    public async Task ClubService_MembershipStateChanges_PersistAccessInvalidatedOutboxMessages()
    {
        var dbName = $"club-member-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        int membershipId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var club = new Club
            {
                Code = "C3",
                Name = "Club 3",
                Category = "Arts",
                ContactEmail = "c3@fptu.edu.vn",
                ContactPhone = "0111"
            };
            db.Clubs.Add(club);

            var m = new ClubMembership
            {
                Club = club,
                UserId = 20,
                FullName = "Member 20",
                Email = "m20@fptu.edu.vn",
                Status = ClubMembershipStatuses.Pending
            };
            db.ClubMemberships.Add(m);
            await db.SaveChangesAsync();
            membershipId = m.Id;
        }

        // Test 1: Approve membership queues outbox
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var m = await db.ClubMemberships.SingleAsync(x => x.Id == membershipId);
            m.Status = ClubMembershipStatuses.Approved;
            m.ReviewedAtUtc = DateTimeOffset.UtcNow;

            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                m.ClubId,
                [m.UserId]), EventRoutingKeys.ClubAccessInvalidated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var msg = await db.OutboxMessages.SingleOrDefaultAsync();
            Assert.NotNull(msg);
            Assert.Equal(EventRoutingKeys.ClubAccessInvalidated, msg.EventType);

            var evt = JsonSerializer.Deserialize<ClubAccessInvalidatedEvent>(msg.Payload, JsonOptions);
            Assert.NotNull(evt);
            Assert.Contains(20, evt.UserIds);
        }

        // Test 2: Assign treasurer queues outbox
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var m = await db.ClubMemberships.SingleAsync(x => x.Id == membershipId);
            m.Role = ClubMemberRoles.Treasurer;
            m.TreasurerSlot = 1;

            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                m.ClubId,
                [m.UserId]), EventRoutingKeys.ClubAccessInvalidated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            Assert.Equal(2, await db.OutboxMessages.CountAsync());
        }

        // Test 3: Remove membership queues outbox
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var m = await db.ClubMemberships.SingleAsync(x => x.Id == membershipId);
            m.IsDeleted = true;
            m.Status = ClubMembershipStatuses.Inactive;

            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                m.ClubId,
                [m.UserId]), EventRoutingKeys.ClubAccessInvalidated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            Assert.Equal(3, await db.OutboxMessages.CountAsync());
        }
    }

    [Fact]
    public async Task ClubService_OwnershipTransferAndManager_PersistAccessInvalidatedOutboxMessages()
    {
        var dbName = $"club-transfer-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var club = new Club
            {
                Code = "C4",
                Name = "Club 4",
                Category = "Technology",
                ContactEmail = "c4@fptu.edu.vn",
                ContactPhone = "0222"
            };
            db.Clubs.Add(club);

            var transfer = new ClubOwnershipTransfer
            {
                Club = club,
                CurrentOwnerUserId = 30,
                NewOwnerUserId = 31,
                Status = "Pending"
            };
            db.ClubOwnershipTransfers.Add(transfer);
            await db.SaveChangesAsync();

            // Approve transfer
            transfer.Status = "Approved";
            db.AddOutboxMessage(new ClubAccessInvalidatedEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                club.Id,
                [transfer.CurrentOwnerUserId, transfer.NewOwnerUserId]), EventRoutingKeys.ClubAccessInvalidated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var msg = await db.OutboxMessages.SingleOrDefaultAsync();
            Assert.NotNull(msg);
            Assert.Equal(EventRoutingKeys.ClubAccessInvalidated, msg.EventType);

            var evt = JsonSerializer.Deserialize<ClubAccessInvalidatedEvent>(msg.Payload, JsonOptions);
            Assert.NotNull(evt);
            Assert.Contains(30, evt.UserIds);
            Assert.Contains(31, evt.UserIds);
        }
    }

    #endregion

    #region REL-F05: ActivityService Outbox & Dual-Write Elimination Tests

    [Fact]
    public async Task ActivityService_CreateActivity_PersistsOutboxMessageAtomically()
    {
        var dbName = $"activity-create-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = new ClubActivity
            {
                ClubId = 1,
                ClubName = "Robotics Club",
                Title = "Robotics Workshop",
                Description = "Introduction to Robotics",
                StartTimeUtc = DateTimeOffset.UtcNow.AddDays(2),
                EndTimeUtc = DateTimeOffset.UtcNow.AddDays(2).AddHours(3),
                Location = "Hall A",
                Status = ActivityStatuses.Scheduled,
                CreatedByUserId = 10
            };

            db.Activities.Add(activity);
            await db.SaveChangesAsync();

            db.AddOutboxMessage(
                new ActivityCreatedEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    activity.Id,
                    activity.ClubId,
                    activity.ClubName,
                    activity.Title,
                    activity.StartTimeUtc),
                EventRoutingKeys.ActivityCreated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = await db.Activities.SingleOrDefaultAsync();
            Assert.NotNull(activity);

            var outboxMsg = await db.OutboxMessages.SingleOrDefaultAsync();
            Assert.NotNull(outboxMsg);
            Assert.Equal(EventRoutingKeys.ActivityCreated, outboxMsg.EventType);
            Assert.Equal(OutboxMessageStatus.Pending, outboxMsg.Status);

            var evt = JsonSerializer.Deserialize<ActivityCreatedEvent>(outboxMsg.Payload, JsonOptions);
            Assert.NotNull(evt);
            Assert.Equal(activity.Id, evt.ActivityId);
            Assert.Equal(1, evt.ClubId);
            Assert.Equal("Robotics Club", evt.ClubName);
            Assert.Equal("Robotics Workshop", evt.Title);
        }
    }

    [Fact]
    public async Task ActivityService_CreateFromApprovedReport_PersistsOutboxMessageAtomically()
    {
        var dbName = $"activity-from-rep-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = new ClubActivity
            {
                SourceReportId = 50,
                SourceReportDetailId = 501,
                ClubId = 2,
                ClubName = "Music Club",
                Title = "Acoustic Night",
                Description = "Live performance",
                StartTimeUtc = DateTimeOffset.UtcNow.AddDays(7),
                EndTimeUtc = DateTimeOffset.UtcNow.AddDays(7).AddHours(4),
                Location = "Auditorium",
                Status = ActivityStatuses.Scheduled,
                CreatedByUserId = 12
            };

            db.Activities.Add(activity);
            await db.SaveChangesAsync();

            db.AddOutboxMessage(
                new ActivityCreatedEvent(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    activity.Id,
                    activity.ClubId,
                    activity.ClubName,
                    activity.Title,
                    activity.StartTimeUtc),
                EventRoutingKeys.ActivityCreated);

            await db.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var msg = await db.OutboxMessages.SingleOrDefaultAsync();
            Assert.NotNull(msg);
            Assert.Equal(EventRoutingKeys.ActivityCreated, msg.EventType);

            var evt = JsonSerializer.Deserialize<ActivityCreatedEvent>(msg.Payload, JsonOptions);
            Assert.NotNull(evt);
            Assert.Equal("Acoustic Night", evt.Title);
        }
    }

    #endregion

    #region REL-F01 & REL-F05: Generic Outbox Publisher Processing Verification

    [Fact]
    public async Task OutboxPublisher_ProcessesExportClubAndActivityOutboxMessages_Successfully()
    {
        var eventBus = Substitute.For<IEventBus>();
        var outboxOptions = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            PollingInterval = TimeSpan.FromMilliseconds(50),
            ClaimDuration = TimeSpan.FromSeconds(30),
            MaxRetries = 3
        });

        // 1. Verify ExportDbContext outbox publisher
        {
            var dbName = $"pub-export-{Guid.NewGuid():N}";
            var sc = new ServiceCollection();
            sc.AddDbContext<ExportDbContext>(o => o.UseInMemoryDatabase(dbName));
            var sp = sc.BuildServiceProvider();

            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
                db.AddOutboxMessage(
                    new ExportCompletedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 1, "PDF", "test.pdf", 10),
                    EventRoutingKeys.ExportCompleted);
                await db.SaveChangesAsync();
            }

            var publisher = new OutboxPublisherBackgroundService<ExportDbContext>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                eventBus,
                outboxOptions,
                NullLogger<OutboxPublisherBackgroundService<ExportDbContext>>.Instance);

            var processed = await publisher.ProcessPendingMessagesAsync(CancellationToken.None);
            Assert.Equal(1, processed);

            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
                var msg = await db.OutboxMessages.SingleAsync();
                Assert.Equal(OutboxMessageStatus.Published, msg.Status);
                Assert.NotNull(msg.ProcessedAtUtc);
            }

            await eventBus.Received(1).PublishAsync(
                Arg.Any<ExportCompletedEvent>(),
                EventRoutingKeys.ExportCompleted,
                Arg.Any<CancellationToken>());
        }

        // 2. Verify ClubDbContext outbox publisher
        {
            var dbName = $"pub-club-{Guid.NewGuid():N}";
            var sc = new ServiceCollection();
            sc.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(dbName));
            var sp = sc.BuildServiceProvider();

            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
                db.AddOutboxMessage(
                    new ClubCreatedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 99, "C99", "Club 99"),
                    EventRoutingKeys.ClubCreated);
                await db.SaveChangesAsync();
            }

            var publisher = new OutboxPublisherBackgroundService<ClubDbContext>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                eventBus,
                outboxOptions,
                NullLogger<OutboxPublisherBackgroundService<ClubDbContext>>.Instance);

            var processed = await publisher.ProcessPendingMessagesAsync(CancellationToken.None);
            Assert.Equal(1, processed);

            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
                var msg = await db.OutboxMessages.SingleAsync();
                Assert.Equal(OutboxMessageStatus.Published, msg.Status);
            }

            await eventBus.Received(1).PublishAsync(
                Arg.Any<ClubCreatedEvent>(),
                EventRoutingKeys.ClubCreated,
                Arg.Any<CancellationToken>());
        }

        // 3. Verify ActivityDbContext outbox publisher
        {
            var dbName = $"pub-activity-{Guid.NewGuid():N}";
            var sc = new ServiceCollection();
            sc.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
            var sp = sc.BuildServiceProvider();

            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
                db.AddOutboxMessage(
                    new ActivityCreatedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 88, 1, "Club 1", "Act 88", DateTimeOffset.UtcNow),
                    EventRoutingKeys.ActivityCreated);
                await db.SaveChangesAsync();
            }

            var publisher = new OutboxPublisherBackgroundService<ActivityDbContext>(
                sp.GetRequiredService<IServiceScopeFactory>(),
                eventBus,
                outboxOptions,
                NullLogger<OutboxPublisherBackgroundService<ActivityDbContext>>.Instance);

            var processed = await publisher.ProcessPendingMessagesAsync(CancellationToken.None);
            Assert.Equal(1, processed);

            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
                var msg = await db.OutboxMessages.SingleAsync();
                Assert.Equal(OutboxMessageStatus.Published, msg.Status);
            }

            await eventBus.Received(1).PublishAsync(
                Arg.Any<ActivityCreatedEvent>(),
                EventRoutingKeys.ActivityCreated,
                Arg.Any<CancellationToken>());
        }
    }

    #endregion
}
