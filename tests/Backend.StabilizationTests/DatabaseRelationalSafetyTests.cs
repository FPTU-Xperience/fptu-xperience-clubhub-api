using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Security;
using AuthService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseSafety_AttendanceRejectsWrongOrMissingSessionAndKeepsLegacyNullSession(bool missing)
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var now = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var first = NewSafetyActivity("First", now.AddDays(-2));
        var second = NewSafetyActivity("Second", now.AddDays(-1));
        db.Activities.AddRange(first, second);
        await db.SaveChangesAsync();
        var session = NewSafetySession(second.Id, now.AddDays(-1));
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var sessionId = session.Id;
        db.ActivityAttendances.Add(new ActivityAttendance
        {
            ActivityId = first.Id,
            SessionId = missing ? int.MaxValue : sessionId,
            UserId = 104,
            AttendanceDate = new DateOnly(2026, 10, 25),
            Status = AttendanceStatuses.Present
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.ActivityAttendances.ToArrayAsync());
        db.ActivityAttendances.AddRange(
            new ActivityAttendance
            {
                ActivityId = first.Id,
                UserId = 104,
                AttendanceDate = new DateOnly(2026, 10, 24),
                Status = AttendanceStatuses.Present
            },
            new ActivityAttendance
            {
                ActivityId = second.Id,
                SessionId = sessionId,
                UserId = 104,
                AttendanceDate = new DateOnly(2026, 10, 25),
                Status = AttendanceStatuses.Late,
                VerificationStatus = "VERIFIED"
            });
        await db.SaveChangesAsync();
        var saved = await db.ActivityAttendances.ToArrayAsync();
        Assert.Equal(2, saved.Length);
        Assert.Single(saved, x => x.ActivityId == first.Id && x.SessionId == null);
        Assert.Single(saved, x => x.ActivityId == second.Id && x.SessionId == sessionId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DatabaseSafety_RevisionLineageIsEnforcedWithoutConstrainingPolymorphicDecisionTarget(
        bool decision, bool missing)
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var first = new VerifiedContribution { UserId = 104, SourceKey = "lineage-first", Title = "First source" };
        var second = new VerifiedContribution { UserId = 104, SourceKey = "lineage-second", Title = "Second source" };
        db.Contributions.AddRange(first, second);
        var firstRevision = new ContributionRevision { ContributionId = first.Id, Ordinal = 1, Status = "SUBMITTED" };
        var secondRevision = new ContributionRevision { ContributionId = second.Id, Ordinal = 1, Status = "SUBMITTED" };
        db.ContributionRevisions.AddRange(firstRevision, secondRevision);
        await db.SaveChangesAsync();
        var badRevision = missing ? Guid.NewGuid() : secondRevision.Id;
        if (decision)
        {
            db.ContributionDecisions.Add(new ContributionDecision
            {
                ContributionId = first.Id,
                RevisionId = badRevision,
                TargetRevisionId = Guid.NewGuid(),
                ReviewerUserId = 200,
                Decision = "APPROVE"
            });
        }
        else
        {
            db.ExperienceLedgerEffects.Add(new ExperienceLedgerEffect
            {
                ContributionId = first.Id,
                RevisionId = badRevision,
                UserId = 104,
                EffectType = "VERIFY",
                Amount = 0
            });
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Empty(await db.ContributionDecisions.ToArrayAsync());
        Assert.Empty(await db.ExperienceLedgerEffects.ToArrayAsync());
        var polymorphicTarget = Guid.NewGuid();
        db.ContributionDecisions.Add(new ContributionDecision
        {
            ContributionId = first.Id,
            RevisionId = firstRevision.Id,
            TargetRevisionId = polymorphicTarget,
            ReviewerUserId = 200,
            Decision = "REVOKE"
        });
        db.ExperienceLedgerEffects.Add(new ExperienceLedgerEffect
        {
            ContributionId = first.Id,
            RevisionId = firstRevision.Id,
            UserId = 104,
            EffectType = "REVOKE",
            Amount = 0
        });
        await db.SaveChangesAsync();
        Assert.Equal(polymorphicTarget, (await db.ContributionDecisions.SingleAsync()).TargetRevisionId);
        Assert.False(await db.ContributionRevisions.AnyAsync(x => x.Id == polymorphicTarget));
        Assert.Equal(firstRevision.Id, (await db.ExperienceLedgerEffects.SingleAsync()).RevisionId);
    }

    [Fact]
    public async Task DatabaseSafety_StartupMigrationCreatesSchemaAndIsSafeToRepeat()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        await db.ApplyMigrationsWithRetryAsync(NullLogger.Instance);
        await db.ApplyMigrationsWithRetryAsync(NullLogger.Instance);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task DatabaseSafety_StartupDoesNotTreatReachablePartialSchemaAsMigrationSuccess()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE [Roles] ([Id] int IDENTITY NOT NULL PRIMARY KEY, [Name] nvarchar(60) NOT NULL)");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.ApplyMigrationsWithRetryAsync(NullLogger.Instance));
        Assert.Equal(2714, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.True(await db.Database.CanConnectAsync());
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());
        await using var sql = new SqlConnection(database.ConnectionString);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.Users', N'U') IS NULL THEN 0 ELSE 1 END";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task DatabaseSafety_EvidenceDenialDoesNotSelectBlobAndOwnerDownloadStillWorks()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var capture = new DatabaseSafetyCommandCapture();
        var builder = ReadEfficiencyBuilder();
        builder.Services.AddDbContext<AdminDbContext>(options => options.UseSqlServer(database.ConnectionString).AddInterceptors(capture));
        builder.Services.AddAuthorization(options => options.AddPolicy(AdminPolicies.AnyActor, policy => policy.RequireAuthenticatedUser()));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPrivateEvidenceEndpoints();
        var bytes = new byte[1024 * 1024];
        "%PDF-"u8.CopyTo(bytes);
        Guid evidenceId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            await db.Database.MigrateAsync();
            var row = new PrivateContributionEvidence
            {
                OwnerUserId = 104,
                Name = "private-proof.pdf",
                ContentType = "application/pdf",
                SizeBytes = bytes.Length,
                Bytes = bytes
            };
            db.ContributionEvidence.Add(row);
            await db.SaveChangesAsync();
            evidenceId = row.Id;
        }
        await app.StartAsync();
        using var outsider = CreateAdminContractClient(app, 999, AuthRoles.ClubManager);
        foreach (var path in new[] { $"/api/contributions/evidence/{evidenceId}", $"/api/contributions/evidence/{evidenceId}/download" })
        {
            capture.Clear();
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
            Assert.Contains(capture.Commands, x => x.Sql.Contains("[ContentType]", StringComparison.Ordinal));
            Assert.DoesNotContain(capture.Commands, x => x.Sql.Contains("[Bytes]", StringComparison.Ordinal));
        }

        using var owner = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        capture.Clear();
        var download = await owner.GetAsync($"/api/contributions/evidence/{evidenceId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", download.Headers.CacheControl?.ToString() ?? "");
        Assert.Single(capture.Commands, x => x.Sql.Contains("[Bytes]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DatabaseSafety_MemberHistoryExcludesCancelledRegardlessOfCase()
    {
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>()
            .UseInMemoryDatabase("member-history-case-" + Guid.NewGuid().ToString("N")).Options);
        var now = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var joined = now.AddDays(-30);
        var activities = Enumerable.Range(1, 7)
            .Select(i => NewSafetyActivity("Case activity " + i, now.AddDays(-i))).ToArray();
        activities[0].PublicationStatus = "LEGACY";
        activities[1].PublicationStatus = "LEGACY";
        activities[5].Status = "cancelled";
        activities[6].Status = "cAnCeLlEd";
        db.Activities.AddRange(activities);
        await db.SaveChangesAsync();
        var unverified = NewSafetySession(activities[2].Id, activities[2].StartTimeUtc);
        var verified = NewSafetySession(activities[3].Id, activities[3].StartTimeUtc);
        db.Sessions.AddRange(unverified, verified);
        await db.SaveChangesAsync();
        db.ActivityAttendances.AddRange(
            NewSafetyAttendance(activities[0].Id, null, "present", verified: false),
            NewSafetyAttendance(activities[1].Id, null, "late", verified: false),
            NewSafetyAttendance(activities[2].Id, unverified.Id, "present", verified: false),
            NewSafetyAttendance(activities[3].Id, verified.Id, "late"),
            NewSafetyAttendance(activities[4].Id, null, "present", userId: 999),
            NewSafetyAttendance(activities[5].Id, null, "present"),
            NewSafetyAttendance(activities[6].Id, null, "late"));
        await db.SaveChangesAsync();

        var service = new MemberActivityStatisticsService(db);
        var batch = await service.GetBatchAsync(1, [new MemberStatisticsInput(104, joined)], now, CancellationToken.None);
        var detail = await service.GetDetailAsync(1, new MemberStatisticsDetailQuery(104, joined, 1, 20), now, CancellationToken.None);
        Assert.Equal(5, detail.Statistics.EligibleActivities);
        Assert.Equal(3, detail.Statistics.AttendedActivities);
        Assert.Equal(60m, detail.Statistics.ParticipationRate);
        Assert.Equal(batch[104], detail.Statistics);
        Assert.Equal(5, detail.TotalItems);
        Assert.Equal(activities.Take(5).Select(x => x.Id), detail.Items.Select(x => x.ActivityId));
        Assert.DoesNotContain(detail.Items, x => string.Equals(x.ActivityStatus, ActivityStatuses.Cancelled, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AttendanceStatuses.NotMarked,
            Assert.Single(detail.Items, x => x.ActivityId == activities[2].Id).AttendanceStatus);
        Assert.Equal(AttendanceStatuses.NotMarked,
            Assert.Single(detail.Items, x => x.ActivityId == activities[4].Id).AttendanceStatus);
    }

    [Fact]
    public async Task DatabaseSafety_MemberHistoryPagesAtSqlAndAggregatesAllEligibleHistory()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var capture = new DatabaseSafetyCommandCapture();
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>()
            .UseSqlServer(database.ConnectionString).AddInterceptors(capture).Options);
        await db.Database.MigrateAsync();
        var now = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var joined = now.AddDays(-60);
        var activities = Enumerable.Range(1, 45).Select(i => NewSafetyActivity("Eligible " + i, now.AddDays(-i))).ToArray();
        var draft = NewSafetyActivity("Draft", now.AddHours(-1)); draft.PublicationStatus = "DRAFT";
        var cancelled = NewSafetyActivity("Cancelled", now.AddHours(-2)); cancelled.Status = ActivityStatuses.Cancelled;
        var otherClub = NewSafetyActivity("Other club", now.AddHours(-3)); otherClub.ClubId = 2;
        db.Activities.AddRange(activities);
        db.Activities.AddRange(draft, cancelled, otherClub,
            NewSafetyActivity("Future", now.AddDays(1)), NewSafetyActivity("Before membership", joined.AddDays(-1)));
        await db.SaveChangesAsync();
        var lateSession = NewSafetySession(activities[1].Id, activities[1].StartTimeUtc);
        var unverifiedSession = NewSafetySession(activities[2].Id, activities[2].StartTimeUtc);
        var duplicatedFirst = NewSafetySession(activities[4].Id, activities[4].StartTimeUtc);
        var duplicatedSecond = NewSafetySession(activities[4].Id, activities[4].StartTimeUtc.AddHours(2));
        db.Sessions.AddRange(lateSession, unverifiedSession, duplicatedFirst, duplicatedSecond);
        await db.SaveChangesAsync();
        db.ActivityAttendances.AddRange(
            NewSafetyAttendance(activities[0].Id, null, AttendanceStatuses.Present),
            NewSafetyAttendance(activities[1].Id, lateSession.Id, AttendanceStatuses.Late),
            NewSafetyAttendance(activities[2].Id, unverifiedSession.Id, AttendanceStatuses.Present, verified: false),
            NewSafetyAttendance(activities[3].Id, null, AttendanceStatuses.Absent),
            NewSafetyAttendance(activities[4].Id, duplicatedFirst.Id, AttendanceStatuses.Present),
            NewSafetyAttendance(activities[4].Id, duplicatedSecond.Id, AttendanceStatuses.Late),
            NewSafetyAttendance(activities[25].Id, null, AttendanceStatuses.Present),
            NewSafetyAttendance(activities[5].Id, null, AttendanceStatuses.Present, userId: 999),
            NewSafetyAttendance(draft.Id, null, AttendanceStatuses.Present));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); capture.Clear();

        var service = new MemberActivityStatisticsService(db);
        var second = await service.GetDetailAsync(1, new MemberStatisticsDetailQuery(104, joined, 2, 20), now, CancellationToken.None);
        Assert.Equal(45, second.Statistics.EligibleActivities);
        Assert.Equal(4, second.Statistics.AttendedActivities);
        Assert.Equal(8.89m, second.Statistics.ParticipationRate);
        Assert.Equal(45, second.TotalItems);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(20, second.Items.Count);
        Assert.Equal(Enumerable.Range(21, 20).Select(i => "Eligible " + i), second.Items.Select(x => x.Title));
        Assert.Equal(AttendanceStatuses.Present, Assert.Single(second.Items, x => x.ActivityId == activities[25].Id).AttendanceStatus);
        var pageQuery = Assert.Single(capture.Commands, x => x.Sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("FETCH NEXT", pageQuery.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(pageQuery.Parameters, x => Equals(x, 20));
        Assert.DoesNotContain(capture.Commands, x => x.Sql.Contains("FROM [Activities]", StringComparison.Ordinal)
            && !x.Sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase)
            && !x.Sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));

        capture.Clear();
        var last = await service.GetDetailAsync(1, new MemberStatisticsDetailQuery(104, joined, 3, 20), now, CancellationToken.None);
        Assert.Equal(second.Statistics, last.Statistics);
        Assert.Equal(5, last.Items.Count);
        Assert.Equal(Enumerable.Range(41, 5).Select(i => "Eligible " + i), last.Items.Select(x => x.Title));
        Assert.All(last.Items, x => Assert.Equal(AttendanceStatuses.NotMarked, x.AttendanceStatus));
        Assert.Single(capture.Commands, x => x.Sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
    }

    private static ClubActivity NewSafetyActivity(string title, DateTimeOffset startsAt) => new()
    {
        ClubId = 1,
        ClubName = "Safety Club",
        Title = title,
        StartTimeUtc = startsAt,
        EndTimeUtc = startsAt.AddHours(1),
        Status = ActivityStatuses.Completed,
        PublicationStatus = "COMPLETED"
    };

    private static ActivitySession NewSafetySession(int activityId, DateTimeOffset startsAt) => new()
    {
        ActivityId = activityId,
        StartTimeUtc = startsAt,
        EndTimeUtc = startsAt.AddHours(1),
        OpensAt = startsAt,
        ClosesAt = startsAt.AddHours(1),
        SemesterId = "FALL2026",
        SeriesId = Guid.NewGuid(),
        CreatedAtUtc = startsAt.AddDays(-1),
        UpdatedAtUtc = startsAt.AddDays(-1)
    };

    private static ActivityAttendance NewSafetyAttendance(int activityId, int? sessionId, string status,
        bool verified = true, int userId = 104) => new()
        {
            ActivityId = activityId,
            SessionId = sessionId,
            UserId = userId,
            AttendanceDate = new DateOnly(2026, 10, 1),
            Status = status,
            VerificationStatus = verified ? "VERIFIED" : "UNVERIFIED"
        };

    private sealed record DatabaseSafetyCommand(string Sql, IReadOnlyCollection<object?> Parameters);

    private sealed class DatabaseSafetyCommandCapture : DbCommandInterceptor
    {
        internal ConcurrentQueue<DatabaseSafetyCommand> Commands { get; } = new();
        internal void Clear() => Commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(new DatabaseSafetyCommand(command.CommandText,
                command.Parameters.Cast<DbParameter>().Select(x => x.Value).ToArray()));
            return new(result);
        }
    }
}
