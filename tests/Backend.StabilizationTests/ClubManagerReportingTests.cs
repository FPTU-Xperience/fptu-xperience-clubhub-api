using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Services;
using AdminService.Data;
using AdminService.Endpoints;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ExportService.Data;
using ExportService.Endpoints;
using ExportService.Models;
using ExportService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task ClubManager_MemberRemovalRejectsStaleVersionReplaysEmptyResponseAndCannotRemoveOwner()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Guid version;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubService.Data.ClubDbContext>();
            version = (await db.ClubMemberships.SingleAsync()).ConcurrencyToken;
            db.ClubMemberships.Add(new ClubService.Models.ClubMembership { Id = 11, ClubId = 1, UserId = 100, Status = "Approved" }); await db.SaveChangesAsync();
        }
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "remove-member");
        Assert.Equal(HttpStatusCode.Conflict, (await manager.DeleteAsync($"/api/clubs/memberships/10?expectedVersion={Guid.NewGuid()}")).StatusCode);
        var path = $"/api/clubs/memberships/10?expectedVersion={version}";
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(path)).StatusCode);
        var replay = await manager.DeleteAsync(path); Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Empty(await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.DeleteAsync("/api/clubs/memberships/11")).StatusCode);
        await using var verify = app.Services.CreateAsyncScope();
        var actual = verify.ServiceProvider.GetRequiredService<ClubService.Data.ClubDbContext>();
        Assert.Equal(1, await actual.OutboxMessages.CountAsync()); Assert.Equal(1, await actual.Set<ClubReportHub.Shared.Data.ApiOperationReceipt>().CountAsync());
        (await actual.ClubManagerAssignments.SingleAsync(x => x.ManagerUserId == 100)).IsActive = false; await actual.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync(path)).StatusCode);
    }

    [Fact]
    public async Task ClubManager_StatisticsCountWholeDatabaseAndFirstParticipationAcrossSourcesWithoutMirrorDuplication()
    {
        var source = ManagerSource(); var planVersion = Guid.NewGuid();
        var participation = Enumerable.Range(120, 125).Select(x => new VerifiedParticipation(x, source.StartTimeUtc)).ToArray();
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient("ExperienceActivity").Returns(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
            new InternalActivityExperienceMetrics(new(2, 1, 3, 2, 125, 125, 0), participation, participation))
        }));
        clients.CreateClient("ExperienceClub").Returns(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                version = planVersion,
                targetActivityCount = 0,
                targetSessionCount = 0,
                targetUniqueParticipantCount = 0
            })
        }));
        await using var app = await CreateManagerExperienceAppAsync(source, services =>
        {
            services.AddSingleton(clients);
            services.AddSingleton(new ClubAccessClient(CreateFakeHttpClient(request =>
            {
                var user = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Subject;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new ClubAccessSnapshot(1, "Club 1", user == "100", false, true, [100], [], []) }) };
            }), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<ClubAccessClient>.Instance));
        });
        app.MapClubExperienceStatisticsEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.Semesters.Add(new Semester { SemesterCode = source.SemesterId, StartDate = source.Policy.StartDate, EndDate = source.Policy.EndDate });
            foreach (var participant in participation)
            {
                var row = new VerifiedContribution
                {
                    UserId = participant.UserId,
                    ClubId = 1,
                    ActivityId = 1,
                    SessionId = 1,
                    SourceKey = "CLUB:1:SESSION:1",
                    SourceId = "1/1",
                    SemesterId = source.SemesterId,
                    Status = "APPROVED",
                    OccurredAt = participant.OccurredAt,
                    ProposedAttributesJson = JsonSerializer.Serialize(ManagerAttributes(), ExperienceJson),
                    EffectiveAttributesJson = JsonSerializer.Serialize(ManagerAttributes(), ExperienceJson)
                };
                db.Contributions.Add(row);
                var effect = new ExperienceLedgerEffect
                {
                    ContributionId = row.Id,
                    RevisionId = Guid.NewGuid(),
                    UserId = row.UserId,
                    SemesterId = source.SemesterId,
                    EffectType = "AWARD",
                    Amount = 1,
                    VerificationId = Guid.NewGuid(),
                    PolicyVersion = source.Policy.PolicyVersion,
                    OccurredAt = row.OccurredAt,
                    RecordedAt = DateTimeOffset.UtcNow
                };
                db.ExperienceLedgerEffects.Add(effect); await db.SaveChangesAsync();
                db.ExperienceLedgerAllocations.Add(new ExperienceLedgerAllocation { EffectId = effect.Id, Pillar = ExperienceCategories.Academic, Amount = 1 });
            }
            db.SelfDeclarations.Add(new SelfDeclaration
            {
                StudentId = 120,
                Title = "Prior external effort",
                Status = "Approved",
                IsOutsideClub = true,
                StartDate = DateOnly.FromDateTime(source.StartTimeUtc.UtcDateTime.AddDays(-2)),
                EndDate = DateOnly.FromDateTime(source.StartTimeUtc.UtcDateTime.AddDays(-2))
            });
            var external = new VerifiedContribution
            {
                UserId = 121,
                SourceType = "EXTERNAL",
                SourceKey = "DECLARATION:TEST",
                Status = "APPROVED",
                OccurredAt = source.StartTimeUtc,
                SemesterId = source.SemesterId
            };
            db.Contributions.Add(external);
            db.SelfDeclarations.Add(new SelfDeclaration
            {
                Id = external.Id,
                StudentId = 121,
                Title = "Mirror of canonical effort",
                Status = "Approved",
                IsOutsideClub = true,
                StartDate = DateOnly.FromDateTime(source.StartTimeUtc.UtcDateTime),
                EndDate = DateOnly.FromDateTime(source.StartTimeUtc.UtcDateTime)
            });
            await db.SaveChangesAsync();
        }
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var outsider = CreateAdminContractClient(app, 101, AuthRoles.ClubManager);
        var path = $"/api/clubs/1/experience-statistics?semesterId={source.SemesterId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        var stats = await manager.GetFromJsonAsync<ClubExperienceStatistics>(path);
        Assert.Equal(125, stats!.ApprovedContributionCount); Assert.Equal(125, stats.UniqueParticipantCount);
        Assert.Equal(124, stats.FirstTimeParticipantCount); Assert.Equal(125, stats.NetPointsByPillar[ExperienceCategories.Academic]);
        Assert.Equal(125, stats.EffectiveRoleCounts[ContributionRoles.Participant]); Assert.All(stats.Progress.Values, value => Assert.Null(value.Ratio));
        Assert.Equal(planVersion, stats.PlanVersion); Assert.NotEmpty(stats.LedgerRevision);
        Assert.Equal(stats.LedgerRevision, (await manager.GetFromJsonAsync<ClubExperienceStatistics>(path))!.LedgerRevision);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.SelfDeclarations.Add(new SelfDeclaration
            {
                StudentId = 122,
                Title = "Earlier external history",
                Status = "Approved",
                IsOutsideClub = true,
                StartDate = DateOnly.FromDateTime(source.StartTimeUtc.UtcDateTime.AddDays(-2)),
                EndDate = DateOnly.FromDateTime(source.StartTimeUtc.UtcDateTime.AddDays(-2))
            });
            await db.SaveChangesAsync();
        }
        var changed = await manager.GetFromJsonAsync<ClubExperienceStatistics>(path);
        Assert.Equal(123, changed!.FirstTimeParticipantCount); Assert.NotEqual(stats.LedgerRevision, changed.LedgerRevision);
    }

    [Fact]
    public async Task ClubManager_MemberStatisticsExcludeDraftAndUnverifiedAttendanceButKeepCompletedVerifiedLateAttendance()
    {
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        db.Activities.AddRange(new ClubActivity { Id = 1, ClubId = 1, StartTimeUtc = now.AddDays(-1), PublicationStatus = "DRAFT" },
            new ClubActivity { Id = 2, ClubId = 1, StartTimeUtc = now.AddDays(-1), PublicationStatus = "COMPLETED" },
            new ClubActivity { Id = 3, ClubId = 1, StartTimeUtc = now.AddDays(-1), PublicationStatus = "PUBLISHED" });
        db.ActivityAttendances.AddRange(new ActivityAttendance { ActivityId = 1, UserId = 104, Status = AttendanceStatuses.Present },
            new ActivityAttendance { ActivityId = 2, SessionId = 2, UserId = 104, Status = AttendanceStatuses.Late, VerificationStatus = "VERIFIED" },
            new ActivityAttendance { ActivityId = 3, SessionId = 3, UserId = 104, Status = AttendanceStatuses.Present, VerificationStatus = "UNVERIFIED" });
        await db.SaveChangesAsync();
        var result = await new MemberActivityStatisticsService(db).GetBatchAsync(1, [new MemberStatisticsInput(104, now.AddDays(-2))], now, CancellationToken.None);
        Assert.Equal(2, result[104].EligibleActivities); Assert.Equal(1, result[104].AttendedActivities); Assert.Equal(50, result[104].ParticipationRate);
    }

    [ClubManagerSqlFact]
    public async Task ClubManager_SqlServerPdfJobsRecoverExpiredLeasePreserveSnapshotAndRecheckOwnershipOnReplayAndDownload()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = SigningKey });
        builder.Services.AddDbContext<ExportDbContext>(o => o.UseSqlServer(database.ConnectionString));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddAuthorization(options => options.AddPolicy(AuthPolicies.BusinessAccess, policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddHostedService<ClubExperienceReportWorker>();
        var ownerActive = true;
        builder.Services.AddSingleton(new ClubAccessClient(CreateFakeHttpClient(request =>
        {
            var user = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Subject;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new ClubAccessSnapshot(1, "Club 1", ownerActive && user == "100", false, true, [100], [], []) }) };
        }), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<ClubAccessClient>.Instance));
        var snapshot = new ClubExperienceStatistics(1, 1, "Club 1", "FALL2026", DateTimeOffset.UtcNow, "VERIFIED_NET_LEDGER", new(1, 1, 1, 1, 1, 1, 0), null,
            new Dictionary<string, TargetProgress>(), 1, 1, new Dictionary<string, int>(), new Dictionary<string, decimal>(), [])
        { LedgerRevision = "TEST_LEDGER_REVISION" };
        var clients = Substitute.For<IHttpClientFactory>(); clients.CreateClient("ExperienceAdmin").Returns(CreateFakeHttpClient(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(snapshot) }));
        builder.Services.AddSingleton(clients);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapClubExperienceReportEndpoints();
        var json = JsonSerializer.Serialize(snapshot, ExperienceJson); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var recoveredId = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>(); await db.Database.MigrateAsync();
            db.ClubExperienceReports.Add(new ClubExperienceReport
            {
                Id = recoveredId,
                ClubId = 1,
                SemesterId = "FALL2026",
                CreatedBy = 100,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SnapshotJson = json,
                SnapshotHash = hash,
                Status = "Processing",
                LeaseUntil = DateTimeOffset.UtcNow.AddMinutes(-1)
            }); await db.SaveChangesAsync();
        }
        await app.StartAsync(); using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var outsider = CreateAdminContractClient(app, 101, AuthRoles.Admin);
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "pdf-request");
        var input = new CreateClubExperienceReportRequest("FALL2026", null, snapshot.LedgerRevision);
        var response = await manager.PostAsJsonAsync("/api/clubs/1/experience-reports", input); Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var reportId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reportId").GetGuid();
        Assert.Equal(HttpStatusCode.Accepted, (await manager.PostAsJsonAsync("/api/clubs/1/experience-reports", input)).StatusCode);
        for (var attempt = 0; attempt < 150; attempt++)
        {
            await using var scope = app.Services.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<ExportDbContext>().ClubExperienceReports.CountAsync(x => x.Status == "Completed") == 2) break;
            await Task.Delay(100);
        }
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>(); var recovered = await db.ClubExperienceReports.SingleAsync(x => x.Id == recoveredId);
            Assert.Equal("Completed", recovered.Status); Assert.Equal(hash, recovered.SnapshotHash); Assert.Equal(json, recovered.SnapshotJson);
            Assert.Equal(2, await db.ClubExperienceReports.CountAsync());
            recovered.SnapshotJson = "{}"; Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }
        var path = $"/api/clubs/1/experience-reports/{reportId}/download";
        var download = await manager.GetAsync(path); Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith("%PDF-"u8)); Assert.Contains("no-store", download.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        ownerActive = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/clubs/1/experience-reports", input)).StatusCode);
    }
}
