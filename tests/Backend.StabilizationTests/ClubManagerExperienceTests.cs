using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Security;
using AdminService.Services;
using AuthService.Data;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Messaging;
using ClubService.Data;
using ExportService.Data;
using ExportService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    private static readonly JsonSerializerOptions ExperienceJson = new(JsonSerializerDefaults.Web);
    private static ContributionAttributes ManagerAttributes(string role = ContributionRoles.Participant) => new("T3", role,
        ActivityScales.Campus, BonusResults.None, [new PillarWeight(ExperienceCategories.Academic, 0.6m), new PillarWeight(ExperienceCategories.Community, 0.4m)]);
    private static ExperienceSourceSnapshot ManagerSource(Guid? policy = null) => new(1, 1, 1, "FALL2026", "Real effort",
        DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), Guid.NewGuid(), Guid.NewGuid(), "SCHEDULED", true,
        ManagerAttributes(), new ExperiencePolicyReference(policy ?? Guid.NewGuid(), "P_B_R_Q_K_V1", "FALL2026",
            DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(2), "APPROVED", "PROJECT", 4, "REVERSE_SOURCE_INVALIDATION"));

    private static async Task SeedManagerPolicyAsync(AdminDbContext db, ExperienceSourceSnapshot source)
    {
        var benchmark = new SemesterBenchmarkConfig { SemesterCode = source.SemesterId, ApprovalStatus = "APPROVED" };
        db.SemesterBenchmarkConfigs.Add(benchmark);
        db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy
        {
            Id = source.Policy.PolicyVersion,
            BenchmarkId = benchmark.Id,
            SemesterId = source.SemesterId,
            Environment = "PROJECT",
            ApprovedBy = 900,
            ApprovalBasis = "TEST FIXTURE ONLY",
            ApprovedAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(new ApprovedExperiencePolicySnapshot(source.Policy,
                benchmark.Id, benchmark.ToTauDictionary(), 20, 40, 60, 30, 0.9m), ExperienceJson)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ClubManager_VerifiedAttendanceDeduplicatesRetriesAndCancellationReversesExactlyOnce()
    {
        await using var db = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var source = ManagerSource(); await SeedManagerPolicyAsync(db, source);
        var handler = new AdminExperienceEventHandler(db, new ConfigurationBuilder().Build());
        var first = new AttendanceVerifiedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 104, 10, "ROTATING_QR", null, source);
        await handler.ProcessAsync(first.EventId, ExperienceRoutingKeys.AttendanceVerified, JsonSerializer.SerializeToElement(first, ExperienceJson), default);
        await handler.ProcessAsync(first.EventId, ExperienceRoutingKeys.AttendanceVerified, JsonSerializer.SerializeToElement(first, ExperienceJson), default);
        var retry = first with { EventId = Guid.NewGuid() };
        await handler.ProcessAsync(retry.EventId, ExperienceRoutingKeys.AttendanceVerified, JsonSerializer.SerializeToElement(retry, ExperienceJson), default);
        Assert.Equal(1, await db.Contributions.CountAsync()); Assert.Equal(1, await db.ExperienceLedgerEffects.CountAsync());
        Assert.Equal(78m, (await db.Contributions.SingleAsync()).RawPoints);
        Assert.Equal(78m, await db.ExperienceLedgerAllocations.SumAsync(x => x.Amount));
        var cancel = new ExperienceSourceCancelledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 1, 1, 1, 100, "Session cancelled.");
        await handler.ProcessAsync(cancel.EventId, ExperienceRoutingKeys.SourceCancelled, JsonSerializer.SerializeToElement(cancel, ExperienceJson), default);
        await handler.ProcessAsync(cancel.EventId, ExperienceRoutingKeys.SourceCancelled, JsonSerializer.SerializeToElement(cancel, ExperienceJson), default);
        Assert.Equal(2, await db.ExperienceLedgerEffects.CountAsync()); Assert.Equal(0m, await db.ExperienceLedgerEffects.SumAsync(x => x.Amount));
        Assert.Equal(0m, await db.ExperienceLedgerAllocations.SumAsync(x => x.Amount));
        Assert.Equal("REVOKED", (await db.Contributions.SingleAsync()).Status);
    }

    [Fact]
    public async Task ClubManager_CancellationBeforeAwardCannotMintPointsAndMissingPolicyFailsTruthfully()
    {
        foreach (var approved in new[] { false, true })
        {
            await using var db = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var source = ManagerSource(); if (approved) await SeedManagerPolicyAsync(db, source);
            var handler = new AdminExperienceEventHandler(db, new ConfigurationBuilder().Build());
            if (approved)
            {
                var cancel = new ExperienceSourceCancelledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 1, null, 1, 100, "Cancelled.");
                await handler.ProcessAsync(cancel.EventId, ExperienceRoutingKeys.SourceCancelled, JsonSerializer.SerializeToElement(cancel, ExperienceJson), default);
            }
            var attendance = new AttendanceVerifiedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 104, 10, "ROTATING_QR", null, source);
            await handler.ProcessAsync(attendance.EventId, ExperienceRoutingKeys.AttendanceVerified, JsonSerializer.SerializeToElement(attendance, ExperienceJson), default);
            Assert.Equal(0, await db.ExperienceLedgerEffects.CountAsync()); Assert.Equal(0, await db.Contributions.CountAsync());
            var receipt = await db.OutboxMessages.SingleAsync(x => x.EventType == ExperienceRoutingKeys.AttendanceRecognition);
            Assert.Contains("FAILED", receipt.Payload); Assert.Contains(approved ? "SOURCE_CANCELLED" : "POLICY_UNAVAILABLE", receipt.Payload);
        }
    }

    [Fact]
    public async Task ClubManager_ContributionReviewsRequireAssignmentAndNoSelfThenUpgradeRecordsOnlyDelta()
    {
        var source = ManagerSource(); await using var app = await CreateManagerExperienceAppAsync(source);
        using var author = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var outsider = CreateAdminContractClient(app, 101, AuthRoles.ClubManager);
        author.DefaultRequestHeaders.Add("Idempotency-Key", "submit");
        var request = new SubmitClubContributionRequest(1, "PARTICIPATION", ManagerAttributes(), "Real evidence", [], source.ActivityVersion);
        var created = await author.PostAsJsonAsync("/api/activities/1/contributions", request); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = (await created.Content.ReadFromJsonAsync<ContributionResponse>())!;
        var review = new ReviewContributionRequest("APPROVE", "Evidence verified.", row.Version, ManagerAttributes());
        manager.DefaultRequestHeaders.Add("Idempotency-Key", "review"); outsider.DefaultRequestHeaders.Add("Idempotency-Key", "review");
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsJsonAsync($"/api/contributions/{row.Id}/reviews", review)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync($"/api/contributions/{row.Id}/reviews", review)).StatusCode);
        var response = await manager.PostAsJsonAsync($"/api/contributions/{row.Id}/reviews", review); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = (await response.Content.ReadFromJsonAsync<ContributionResponse>())!; Assert.Equal(78m, approved.RawPoints);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"/api/contributions/{row.Id}/reviews", review)).StatusCode);
        author.DefaultRequestHeaders.Remove("Idempotency-Key"); author.DefaultRequestHeaders.Add("Idempotency-Key", "upgrade");
        var upgradeResponse = await author.PostAsJsonAsync($"/api/contributions/{row.Id}/role-upgrade-requests",
            new RequestContributionUpgradeRequest("Product", null, [], "Produced a deliverable.", approved.Version));
        Assert.Equal(HttpStatusCode.Created, upgradeResponse.StatusCode);
        var upgrade = (await upgradeResponse.Content.ReadFromJsonAsync<ContributionUpgradeRequest>())!;
        manager.DefaultRequestHeaders.Remove("Idempotency-Key"); manager.DefaultRequestHeaders.Add("Idempotency-Key", "upgrade-review");
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"/api/contributions/{row.Id}/role-upgrade-requests/{upgrade.Id}/reviews",
            new ReviewContributionRequest("APPROVE", "Product verified.", upgrade.Version))).StatusCode);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        Assert.Equal(1, await db.Contributions.CountAsync()); Assert.Equal(2, await db.ExperienceLedgerEffects.CountAsync());
        Assert.Equal(93.6m, (await db.ExperienceLedgerEffects.SingleAsync(x => x.EffectType == "UPGRADE_DELTA")).Amount);
        Assert.Equal(171.6m, await db.ExperienceLedgerEffects.SumAsync(x => x.Amount));
        (await db.VerificationAssignments.SingleAsync()).IsRevoked = true; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/contributions/{row.Id}/reviews", review)).StatusCode);
    }

    [Fact]
    public async Task ClubManager_ManualRequestCannotSelfApproveAndApprovalRemainsPendingUntilSourceConfirmation()
    {
        var source = ManagerSource(); await using var app = await CreateManagerExperienceAppAsync(source);
        using var author = CreateAdminContractClient(app, 104, AuthRoles.ClubMember); using var reviewer = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        author.DefaultRequestHeaders.Add("Idempotency-Key", "manual");
        var created = await author.PostAsJsonAsync("/api/activities/1/sessions/1/manual-attendance-requests", new SubmitManualAttendanceRequest("GPS unavailable."));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); var row = (await created.Content.ReadFromJsonAsync<ManualAttendanceRequest>())!;
        var request = new ReviewContributionRequest("APPROVE", "Presence confirmed.", row.Version);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsJsonAsync($"/api/activities/1/sessions/1/manual-attendance-requests/{row.Id}/review", request)).StatusCode);
        reviewer.DefaultRequestHeaders.Add("Idempotency-Key", "manual-review");
        var reviewed = await reviewer.PostAsJsonAsync($"/api/activities/1/sessions/1/manual-attendance-requests/{row.Id}/review", request);
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode); Assert.Equal("PENDING", (await reviewed.Content.ReadFromJsonAsync<ManualAttendanceRequest>())!.RecognitionStatus);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        Assert.Equal(0, await db.ExperienceLedgerEffects.CountAsync()); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == ExperienceRoutingKeys.ManualAttendanceDecision));
    }

    [Theory]
    [InlineData(0.3333, 0.3333, 0.3334)]
    [InlineData(0.1, 0.2, 0.7)]
    public void ClubManager_PillarRoundingConservesTotalAndRejectsInvalidWeights(double first, double second, double third)
    {
        var weights = new[] { new PillarWeight(ExperienceCategories.Academic, (decimal)first), new PillarWeight(ExperienceCategories.Community, (decimal)second), new PillarWeight(ExperienceCategories.Global, (decimal)third) };
        Assert.Equal(78m, VerifiedExperienceRules.Allocate(78, weights).Values.Sum());
        Assert.Null(VerifiedExperienceRules.Validate(ManagerAttributes() with { PillarWeights = weights }));
        Assert.NotNull(VerifiedExperienceRules.Validate(ManagerAttributes() with { PillarWeights = [new PillarWeight(ExperienceCategories.Academic, 0.8m)] }));
    }

    [Fact]
    public void ClubManager_ExperiencePdfIsRealDocumentWithNoContactsOrEvidence()
    {
        var stats = new ClubExperienceStatistics(1, 1, "Test Club", "FALL2026", DateTimeOffset.UtcNow, "VERIFIED_NET_LEDGER",
            new ActivityExperienceMetrics(1, 1, 5, 5, 5, 1, 0), new ClubExperienceTargets(Guid.NewGuid(), 1, 5, 0),
            new Dictionary<string, TargetProgress> { ["participants"] = new(1, 0, null) }, 1, 1,
            new Dictionary<string, int> { ["Participant"] = 1 }, new Dictionary<string, decimal> { ["ACAD"] = 78 }, []);
        var bytes = ClubExperienceReportWorker.Generate(stats, new string('A', 64));
        Assert.True(bytes.AsSpan().StartsWith("%PDF-"u8)); Assert.True(bytes.Length > 1000);
        Assert.DoesNotContain("privateEvidence", JsonSerializer.Serialize(stats, ExperienceJson));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void ClubManager_PublicImageDecoderRejectsForgedAndBrokenFiles(string contentType) =>
        Assert.Throws<InvalidDataException>(() => PublicMediaStorage.Validate("<svg onload='alert(1)'/>"u8.ToArray(), contentType));

    [Fact]
    public async Task ClubManager_PrivateEvidenceRequiresOwningAuthorOrCurrentAssignedSource()
    {
        var source = ManagerSource(); await using var app = await CreateManagerExperienceAppAsync(source);
        using var author = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var outsider = CreateAdminContractClient(app, 101, AuthRoles.Admin);
        author.DefaultRequestHeaders.Add("Idempotency-Key", "private-evidence");
        using var form = new MultipartFormDataContent();
        var bytes = ClubExperienceReportWorker.Generate(new ClubExperienceStatistics(1, 1, "Club 1", "FALL2026", DateTimeOffset.UtcNow,
            "VERIFIED_NET_LEDGER", new ActivityExperienceMetrics(0, 0, 0, 0, 0, 0, 0), null, new Dictionary<string, TargetProgress>(),
            0, 0, new Dictionary<string, int>(), new Dictionary<string, decimal>(), []), new string('A', 64));
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new("application/pdf"); form.Add(file, "file", "proof.pdf");
        var uploaded = await author.PostAsync("/api/contributions/evidence", form); Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var id = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attachmentId").GetGuid();
        var path = $"/api/contributions/evidence/{id}/download";
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        author.DefaultRequestHeaders.Remove("Idempotency-Key"); author.DefaultRequestHeaders.Add("Idempotency-Key", "with-proof");
        var submitted = await author.PostAsJsonAsync("/api/activities/1/contributions", new SubmitClubContributionRequest(1, "EVIDENCE",
            ManagerAttributes(), "Private proof.", [id], source.ActivityVersion));
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var downloaded = await manager.GetAsync(path); Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(bytes, await downloaded.Content.ReadAsByteArrayAsync()); Assert.Contains("no-store", downloaded.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/activities/999/contributions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/activities/1/contributions")).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>(); (await db.VerificationAssignments.SingleAsync()).IsRevoked = true; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync(path)).StatusCode);
    }

    private static async Task<WebApplication> CreateManagerExperienceAppAsync(ExperienceSourceSnapshot source,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = SigningKey, ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken });
        var database = Guid.NewGuid().ToString(); builder.Services.AddDbContext<AdminDbContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentActor>(services =>
        {
            var accessor = services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var actor = Substitute.For<ICurrentActor>();
            actor.UserId.Returns(_ => accessor.HttpContext?.User.GetUserId());
            actor.Roles.Returns(_ => accessor.HttpContext?.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray() ?? []);
            actor.IsAuthenticated.Returns(_ => accessor.HttpContext?.User.Identity?.IsAuthenticated == true);
            actor.CampusCode.Returns("HN"); return actor;
        });
        builder.Services.AddSingleton(Substitute.For<AdminService.Auditing.IAuditService>());
        builder.Services.AddAuthorization(o => { o.AddPolicy(AdminPolicies.AnyActor, p => p.RequireAuthenticatedUser()); o.AddPolicy(AdminPolicies.AdminOnly, p => p.RequireRole(AuthRoles.Admin)); o.AddPolicy(AdminPolicies.BackofficeUser, p => p.RequireRole(AuthRoles.Admin, AuthRoles.StudentAffairsAdmin)); });
        builder.Services.AddSingleton(new ActivitySourceClient(CreateFakeHttpClient(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith("/scope", StringComparison.Ordinal)
            ? JsonContent.Create(new ActivityExperienceScope(source.ActivityId, request.RequestUri.AbsolutePath.Contains("/999/", StringComparison.Ordinal) ? 999 : source.ClubId))
            : JsonContent.Create(new ActivitySourceContext(source, true, false))
        }), builder.Configuration));
        configure?.Invoke(builder.Services);
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapVerifiedContributionEndpoints(); app.MapManualAttendanceEndpoints(); app.MapPrivateEvidenceEndpoints(); app.MapDeclarationEndpoints(); app.MapBenchmarkEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>(); await SeedManagerPolicyAsync(db, source);
            db.VerificationAssignments.Add(new VerificationAssignment
            {
                UserId = 100,
                ClubId = 1,
                SourceType = "CLUB",
                StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
                CapabilitiesJson = "[\"contribution.review\",\"attendance.verify\",\"experience.adjust\"]",
                AssignedBy = 900,
                Reason = "TEST FIXTURE ONLY",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }
}

public sealed class ClubManagerSqlFactAttribute : FactAttribute
{
    public ClubManagerSqlFactAttribute()
    {
        if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")))
            Skip = "SQL Server test connection is not configured.";
    }
}

public sealed partial class ClubManagerSqlIntegrityTests
{
    [ClubManagerSqlFact]
    public async Task ClubManager_AllFiveDatabasesMigrateAndReloadOnSqlServer()
    {
        foreach (var service in new[] { "Club", "Activity", "Admin", "Auth", "Export" })
        {
            await using var database = await ManagerSqlDatabase.CreateAsync();
            await using var db = Context(service, database.ConnectionString); await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
    }

    [ClubManagerSqlFact]
    public async Task ClubManager_SqlServerUniqueSourceAndLedgerImmutabilitySurviveReload()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using (var db = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseSqlServer(database.ConnectionString).Options))
        {
            await db.Database.MigrateAsync();
            db.Contributions.Add(new VerifiedContribution { UserId = 104, SourceKey = "CLUB:1:SESSION:1", ProposedAttributesJson = "{}", SourceSnapshotJson = "{}" });
            await db.SaveChangesAsync();
        }
        await using var second = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseSqlServer(database.ConnectionString).Options);
        second.Contributions.Add(new VerifiedContribution { UserId = 104, SourceKey = "CLUB:1:SESSION:1", ProposedAttributesJson = "{}", SourceSnapshotJson = "{}" });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        second.ChangeTracker.Clear(); Assert.Equal(1, await second.Contributions.CountAsync());
        var contribution = await second.Contributions.SingleAsync();
        var effect = new ExperienceLedgerEffect
        {
            ContributionId = contribution.Id,
            RevisionId = Guid.NewGuid(),
            UserId = 104,
            SemesterId = "FALL2026",
            EffectType = "AWARD",
            Amount = 78,
            VerificationId = Guid.NewGuid(),
            PolicyVersion = Guid.NewGuid(),
            FormulaVersion = "TEST_ONLY",
            OccurredAt = DateTimeOffset.UtcNow,
            RecordedAt = DateTimeOffset.UtcNow
        };
        second.ExperienceLedgerEffects.Add(effect); await second.SaveChangesAsync();
        second.ChangeTracker.Clear();
        var saved = await second.ExperienceLedgerEffects.SingleAsync(); saved.Amount = 999;
        Assert.Throws<InvalidOperationException>(() => second.SaveChanges(false));
        second.ChangeTracker.Clear(); saved = await second.ExperienceLedgerEffects.SingleAsync();
        second.ExperienceLedgerEffects.Remove(saved);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.SaveChangesAsync(false));
        second.ChangeTracker.Clear(); Assert.Equal(78, (await second.ExperienceLedgerEffects.SingleAsync()).Amount);
    }

    [ClubManagerSqlFact]
    public async Task ClubManager_SqlServerFailedMutationRollsBackSourceOutboxAndReceiptAfterHandlerSave()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseSqlServer(database.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "104")], "TEST_ONLY"));
        http.Request.Method = "POST"; http.Request.Path = "/api/contributions/test-rollback"; http.Request.Headers["Idempotency-Key"] = "rollback";
        var result = await ApiOperations.ExecuteAsync(http, db, new { request = 1 }, async () =>
        {
            db.Contributions.Add(new VerifiedContribution { UserId = 104, SourceKey = "ROLLBACK_TEST", ProposedAttributesJson = "{}", SourceSnapshotJson = "{}" });
            db.AddOutboxMessage(new ExperienceSourceCancelledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, 1, 1, 1, 100, "Rollback test"), ExperienceRoutingKeys.SourceCancelled);
            await db.SaveChangesAsync();
            return ApiOperations.Error(409, "TEST_CONFLICT", "Intentional rejection after save.");
        }, CancellationToken.None);
        Assert.Equal(409, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)result).StatusCode);
        await using var reloaded = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseSqlServer(database.ConnectionString).Options);
        Assert.Equal(0, await reloaded.Contributions.CountAsync()); Assert.Equal(0, await reloaded.OutboxMessages.CountAsync());
        Assert.Equal(0, await reloaded.Set<ApiOperationReceipt>().CountAsync());
    }

    [ClubManagerSqlFact]
    public async Task ClubManager_SqlServerLegacyUpgradePreservesHistoryWithoutVerifiedPoints()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>().UseSqlServer(database.ConnectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260921220616_AddActivityTransactionalOutbox");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Activities (ClubId,ClubName,Title,Description,StartTimeUtc,EndTimeUtc,MeetingDaysCsv,Location,Status,CreatedByUserId,CreatedAtUtc,UpdatedAtUtc)
            VALUES (1,'Historical club','Historical event','Historical record',SYSUTCDATETIME(),SYSUTCDATETIME(),'','Campus','Completed',100,SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT INTO ActivityParticipants (ActivityId,UserId,FullName,AttendanceStatus,RegisteredAtUtc) VALUES (1,104,'Historical member','Present',SYSUTCDATETIME());
            INSERT INTO ActivityAttendances (ActivityId,UserId,FullName,AttendanceDate,Status,CreatedAtUtc) VALUES (1,104,'Historical member','2026-10-12','Present',SYSUTCDATETIME());
            """);
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var activity = await db.Activities.SingleAsync(); var attendance = await db.ActivityAttendances.SingleAsync();
        Assert.Equal("LEGACY", activity.PublicationStatus); Assert.Equal(1, activity.RegisteredCount); Assert.NotEqual(Guid.Empty, activity.Version);
        Assert.Null(attendance.SessionId); Assert.Equal("UNVERIFIED", attendance.VerificationStatus); Assert.Equal("NOT_REQUESTED", attendance.RecognitionStatus);
        Assert.Equal(0, await db.OutboxMessages.CountAsync());
    }

    private static DbContext Context(string service, string connection) => service switch
    {
        "Club" => new ClubDbContext(new DbContextOptionsBuilder<ClubDbContext>().UseSqlServer(connection).Options),
        "Activity" => new ActivityDbContext(new DbContextOptionsBuilder<ActivityDbContext>().UseSqlServer(connection).Options),
        "Admin" => new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseSqlServer(connection).Options),
        "Auth" => new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(connection).Options),
        _ => new ExportDbContext(new DbContextOptionsBuilder<ExportDbContext>().UseSqlServer(connection).Options)
    };
}

internal sealed class ManagerSqlDatabase : IAsyncDisposable
{
    private readonly string master;
    private readonly string name;
    internal string ConnectionString { get; }
    private ManagerSqlDatabase(string root, string database)
    {
        master = root; name = database; var builder = new SqlConnectionStringBuilder(root) { InitialCatalog = name }; ConnectionString = builder.ConnectionString;
    }
    internal static async Task<ManagerSqlDatabase> CreateAsync()
    {
        var connection = Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")
            ?? "Server=(localdb)\\ClubManagerApiTests;Database=master;Integrated Security=True;TrustServerCertificate=True";
        var root = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString;
        var name = "ClubManagerApiTests_" + Guid.NewGuid().ToString("N");
        await using var sql = new SqlConnection(root); await sql.OpenAsync(); using var command = sql.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{name}]"; await command.ExecuteNonQueryAsync(); return new(root, name);
    }
    public async ValueTask DisposeAsync()
    {
        if (!name.StartsWith("ClubManagerApiTests_", StringComparison.Ordinal) || !Guid.TryParseExact(name[20..], "N", out _))
            throw new InvalidOperationException("Refusing to remove a database outside the task-owned test prefix.");
        using (var pool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pool);
        await using var sql = new SqlConnection(master); await sql.OpenAsync(); using var command = sql.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]";
        await command.ExecuteNonQueryAsync();
    }
}
