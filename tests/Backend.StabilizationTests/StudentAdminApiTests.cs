using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Models;
using AdminService.Contracts;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Student_RadarUsesNetPeriodLedgerCumulativePolicyAndExcludesLegacy()
    {
        await using var app = await CreateStudentAdminAppAsync();
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 60);
            await SeedStudentContributionAsync(db, 104, "SP26", "Research", 25, activityId: 2);
            db.SelfDeclarations.Add(new SelfDeclaration
            {
                StudentId = 104,
                Title = "Legacy preview",
                Category = "Academic",
                Status = "Approved",
                RawPoints = 999,
                StartDate = new(2026, 1, 1),
                EndDate = new(2026, 1, 1)
            });
            await db.SaveChangesAsync();
        }
        var period = await client.GetFromJsonAsync<JsonElement>("/api/v1/declarations/me/radar?semester=FA26");
        Assert.Equal(60, period.GetProperty("radarAxes")[0].GetProperty("rawPointsSum").GetDecimal());
        Assert.Equal(0, period.GetProperty("radarAxes")[1].GetProperty("rawPointsSum").GetDecimal());
        Assert.True(period.GetProperty("isAuthoritative").GetBoolean());
        Assert.Equal(1, period.GetProperty("legacyPreviewCount").GetInt32());
        Assert.Equal(6, period.GetProperty("radarAxes").GetArrayLength());
        Assert.Equal("RealWorldWork", period.GetProperty("plusOne").GetProperty("pillar").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/declarations/me/radar?scope=CUMULATIVE")).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope()) await SeedStudentCumulativePolicyAsync(scope.ServiceProvider.GetRequiredService<AdminDbContext>());
        var cumulative = await client.GetFromJsonAsync<JsonElement>("/api/v1/declarations/me/radar?scope=CUMULATIVE");
        Assert.Equal(60, cumulative.GetProperty("radarAxes")[0].GetProperty("rawPointsSum").GetDecimal());
        Assert.Equal(25, cumulative.GetProperty("radarAxes")[1].GetProperty("rawPointsSum").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/declarations/me/radar?semester=MISSING")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/declarations/me/radar?scope=CUMULATIVE&semester=FA26")).StatusCode);
    }

    [Fact]
    public async Task Student_SummaryCountsAllRecordsAndShowsUnavailableSourcesInsteadOfZero()
    {
        var state = new StudentSourceState { Unavailable = true };
        await using var app = await CreateStudentAdminAppAsync(state);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            for (var i = 0; i < 105; i++) await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 10, activityId: i + 1);
        }
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/users/me/experience-summary?semesterId=FA26");
        Assert.Equal(105, summary.GetProperty("recognizedContributionCount").GetInt32());
        Assert.Equal(1050, summary.GetProperty("rawExperiencePoints").GetDecimal());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("verifiedAttendanceCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("approvedClubCount").ValueKind);
        Assert.False(summary.GetProperty("availability").GetProperty("attendance").GetBoolean());
        state.Unavailable = false;
        summary = await client.GetFromJsonAsync<JsonElement>("/api/users/me/experience-summary?semesterId=FA26");
        Assert.Equal(2, summary.GetProperty("verifiedAttendanceCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("approvedClubCount").GetInt32());
    }

    [Fact]
    public async Task Student_RecommendationsRankLowestAxisAcrossAllCandidatePagesAndNeverAwardPoints()
    {
        var state = new StudentSourceState();
        await using var app = await CreateStudentAdminAppAsync(state);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 400);
        }
        var start = DateTimeOffset.UtcNow.AddDays(2);
        state.Activities = Enumerable.Range(1, 101).Select(i => new StudentActivityItem(i, 1, "C1", "Club 1",
            "Activity " + i, "Safe description", null, start.AddMinutes(i), start.AddMinutes(i + 60), "Campus",
            "UPCOMING", "REGISTER", true, true, "FA26", i, Guid.NewGuid(), Guid.NewGuid(), ManagerSource().Policy.PolicyVersion,
            [new PillarWeight(i == 101 ? "Research" : "Academic", 1)])).ToArray();
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v2/recommendations/activities?semesterId=FA26&limit=1");
        Assert.Equal(101, response.GetProperty("items")[0].GetProperty("activity").GetProperty("id").GetInt32());
        Assert.Equal("Research", response.GetProperty("items")[0].GetProperty("targetPillar").GetString());
        await using var verify = app.Services.CreateAsyncScope();
        Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<AdminDbContext>().ExperienceLedgerEffects.CountAsync());
        state.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v2/recommendations/activities?semesterId=FA26")).StatusCode);
    }

    [Fact]
    public async Task Student_QuestCompletionRequiresSelfParticipationVerifiedProofAndCannotAddPointsOrReplayRevokedProof()
    {
        await using var app = await CreateStudentAdminAppAsync();
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var other = CreateAdminContractClient(app, 105, AuthRoles.ClubMember);
        Guid questId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            var policy = await db.ApprovedExperiencePolicies.SingleAsync();
            var quest = new Quest
            {
                SemesterCode = "FA26",
                CampusCode = "GLOBAL",
                Title = "Verified quest",
                Description = "Source proof",
                CriteriaVersion = Guid.NewGuid(),
                CriteriaSnapshotJson = JsonSerializer.Serialize(new QuestCriteria("Academic", [1], policy.Id), ExperienceJson)
            };
            questId = quest.Id; db.Quests.Add(quest); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", new CompleteQuestRequest(null, null))).StatusCode);
        var joined = await client.PostAsJsonAsync($"/api/v1/quests/{questId}/join", new JoinQuestRequest(null));
        Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        var participation = await joined.Content.ReadFromJsonAsync<QuestParticipantResponse>(ExperienceJson);
        Assert.NotNull(participation);
        var request = new CompleteQuestRequest(null, "Server checks proofs") { ExpectedVersion = participation.Version };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/quests/{questId}/complete", request with { StudentUserId = 104 })).StatusCode);
        Guid proofId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            proofId = await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 60);
        }
        client.DefaultRequestHeaders.Add("Idempotency-Key", "student-complete");
        var completed = await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request)).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            Assert.Equal(1, await db.ExperienceLedgerEffects.CountAsync());
            Assert.Equal(1, await db.QuestCompletionRecords.CountAsync());
            var contribution = await db.Contributions.SingleAsync(x => x.Id == proofId);
            contribution.Status = "REVOKED"; contribution.RawPoints = 0;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request)).StatusCode);
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests/me?page=1&pageSize=20");
        Assert.Equal("REQUIRES_REVALIDATION", mine.GetProperty("items")[0].GetProperty("effectiveStatus").GetString());
    }

    [Fact]
    public async Task Student_ContributionPagingDeduplicatesMirrorsAndKeepsLegacyUnassigned()
    {
        await using var app = await CreateStudentAdminAppAsync();
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            var id = await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 60);
            db.SelfDeclarations.Add(new SelfDeclaration { Id = id, StudentId = 104, Title = "Mirror", Category = "Academic" });
            db.SelfDeclarations.Add(new SelfDeclaration { StudentId = 104, Title = "Legacy", Category = "Academic" });
            db.SelfDeclarations.Add(new SelfDeclaration { StudentId = 105, Title = "Other student", Category = "Academic" });
            await db.SaveChangesAsync();
        }
        var page = await client.GetFromJsonAsync<JsonElement>("/api/users/me/contributions?page=1&pageSize=1");
        Assert.Equal(2, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/users/me/contributions?semesterId=FA26");
        Assert.Equal(1, filtered.GetProperty("totalItems").GetInt32());
    }

    [ClubManagerSqlFact]
    public async Task Student_SqlServerRadarAndUnionPagingRunAgainstAppliedMigrations()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateStudentAdminAppAsync(connection: database.ConnectionString);
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 60);
            db.SelfDeclarations.Add(new SelfDeclaration { StudentId = 104, Title = "Historical", Category = "Academic" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/declarations/me/radar?semester=FA26")).StatusCode);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/users/me/contributions?page=1&pageSize=1");
        Assert.Equal(2, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
    }

    private static async Task<WebApplication> CreateStudentAdminAppAsync(StudentSourceState? state = null, string? connection = null)
    {
        state ??= new StudentSourceState();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        });
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AdminDbContext>(o => { if (connection is null) o.UseInMemoryDatabase(database); else o.UseSqlServer(connection); });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentActor>(services =>
        {
            var accessor = services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var actor = Substitute.For<ICurrentActor>();
            actor.UserId.Returns(_ => accessor.HttpContext?.User.GetUserId());
            actor.IsAuthenticated.Returns(_ => accessor.HttpContext?.User.Identity?.IsAuthenticated == true);
            actor.CampusCode.Returns("HN"); return actor;
        });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(AdminPolicies.AnyActor, p => p.RequireAuthenticatedUser());
            o.AddPolicy(AdminPolicies.AdminOnly, p => p.RequireRole(AuthRoles.Admin));
            o.AddPolicy(AdminPolicies.BackofficeUser, p => p.RequireRole(AuthRoles.Admin, AuthRoles.StudentAffairsAdmin));
        });
        builder.Services.AddSingleton(new CurrentUserProfileClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { new InternalUserProfile(104, "Canonical student", "student@example.invalid", "HN", true, false, null),
            new InternalUserProfile(105, "Other student", "other@example.invalid", "HCM", true, false, null) })
        }), builder.Configuration));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(call => CreateFakeHttpClient(request =>
        {
            if (state.Unavailable) return new(HttpStatusCode.ServiceUnavailable);
            if (request.RequestUri!.AbsolutePath.EndsWith("/counters", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new StudentCounters(104, 2, 1, DateTimeOffset.UtcNow, "TEST_SOURCE_ONLY")) };
            if (request.RequestUri.AbsolutePath.EndsWith("/source-status", StringComparison.Ordinal))
            {
                var proof = request.Content!.ReadFromJsonAsync<StudentSourceStatusQuery>().GetAwaiter().GetResult()!;
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new StudentSourceStatus(
                    proof.Sources.Where(x => !state.CancelledSources.Contains(x)).ToArray(), DateTimeOffset.UtcNow))
                };
            }
            var query = request.Content!.ReadFromJsonAsync<StudentCandidateQuery>().GetAwaiter().GetResult()!;
            state.CandidateObserver?.Invoke(query);
            var items = state.Activities.Where(x => query.ActivityIds is null || query.ActivityIds.Contains(x.Id)).ToArray();
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new StudentActivityPage(items.Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize).ToArray(), query.Page, query.PageSize, items.Length,
                (int)Math.Ceiling((decimal)items.Length / query.PageSize), DateTimeOffset.UtcNow, "TEST_SCOPE_ONLY"))
            };
        }));
        builder.Services.AddSingleton(factory); builder.Services.AddScoped<StudentExperienceSourceClient>();
        var source = ManagerSource();
        source = source with { SemesterId = "FA26", Policy = source.Policy with { SemesterId = "FA26" } };
        builder.Services.AddSingleton(new ActivitySourceClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new ActivitySourceContext(source, true, false)) }), builder.Configuration));
        builder.Services.AddSingleton(Substitute.For<AdminService.Auditing.IAuditService>());
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapStudentExperienceEndpoints(); app.MapBenchmarkEndpoints(); app.MapVerifiedContributionEndpoints(); app.MapQuestEndpoints(); app.MapDeclarationEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            if (connection is not null) await db.Database.MigrateAsync();
            await SeedManagerPolicyAsync(db, source);
            db.Semesters.Add(new Semester { SemesterCode = "FA26", IsActive = true, StartDate = DateTimeOffset.UtcNow.AddMonths(-3), EndDate = DateTimeOffset.UtcNow.AddMonths(3) });
            db.Semesters.Add(new Semester { SemesterCode = "SP26", IsActive = false, StartDate = DateTimeOffset.UtcNow.AddMonths(-9), EndDate = DateTimeOffset.UtcNow.AddMonths(-4) });
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }

    private static async Task<Guid> SeedStudentContributionAsync(AdminDbContext db, int userId, string semester, string pillar,
        decimal points, int activityId = 1)
    {
        var policy = await db.ApprovedExperiencePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Scope == "PERIOD" && x.SemesterId == semester);
        if (policy is null)
        {
            var benchmark = new SemesterBenchmarkConfig { SemesterCode = semester, ApprovalStatus = "APPROVED" };
            db.SemesterBenchmarkConfigs.Add(benchmark);
            var reference = ManagerSource().Policy with { PolicyVersion = Guid.NewGuid(), SemesterId = semester };
            policy = new ApprovedExperiencePolicy
            {
                Id = reference.PolicyVersion,
                BenchmarkId = benchmark.Id,
                SemesterId = semester,
                ApprovalBasis = "TEST FIXTURE ONLY",
                ApprovedAt = DateTimeOffset.UtcNow,
                SnapshotJson = JsonSerializer.Serialize(new ApprovedExperiencePolicySnapshot(reference, benchmark.Id,
                    benchmark.ToTauDictionary(), 20, 50, 80, 20, 0.9m), ExperienceJson)
            };
            db.ApprovedExperiencePolicies.Add(policy); await db.SaveChangesAsync();
        }
        var tier = points switch { 10m => "T1", 25m => "T2", 60m => "T3", 400m => "T5", _ => throw new ArgumentOutOfRangeException(nameof(points)) };
        var attributes = new ContributionAttributes(tier, "Participant", "Club", "None", [new PillarWeight(pillar, 1)]);
        var row = new VerifiedContribution
        {
            UserId = userId,
            SemesterId = semester,
            ActivityId = activityId,
            SessionId = activityId,
            SourceId = $"{activityId}/{activityId}",
            SourceKey = $"CLUB:{activityId}:SESSION:{activityId}",
            Title = "Verified source",
            Status = "APPROVED",
            RawPoints = points,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProposedAttributesJson = JsonSerializer.Serialize(attributes, ExperienceJson),
            EffectiveAttributesJson = JsonSerializer.Serialize(attributes, ExperienceJson),
            PolicySnapshotJson = policy.SnapshotJson,
            SourceSnapshotJson = "{}"
        };
        var revision = new ContributionRevision
        {
            ContributionId = row.Id,
            Ordinal = 1,
            Status = "APPROVED",
            AttributesJson = row.EffectiveAttributesJson,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        row.CurrentRevisionId = revision.Id; row.RevisionOrdinal = 1;
        db.Contributions.Add(row); db.ContributionRevisions.Add(revision);
        var effect = new ExperienceLedgerEffect
        {
            ContributionId = row.Id,
            RevisionId = revision.Id,
            UserId = userId,
            SemesterId = semester,
            EffectType = "APPROVED",
            Amount = points,
            PolicyVersion = policy.Id,
            FormulaVersion = "P_B_R_Q_K_V1",
            RecordedAt = DateTimeOffset.UtcNow,
            OccurredAt = row.OccurredAt,
            VerificationId = Guid.NewGuid(),
            VerificationMethod = "TEST_FIXTURE_ONLY"
        };
        db.ExperienceLedgerEffects.Add(effect); await db.SaveChangesAsync();
        db.ExperienceLedgerAllocations.Add(new ExperienceLedgerAllocation { EffectId = effect.Id, Pillar = pillar, Amount = points });
        await db.SaveChangesAsync(); return row.Id;
    }

    private static async Task SeedStudentCumulativePolicyAsync(AdminDbContext db)
    {
        var original = await db.ApprovedExperiencePolicies.AsNoTracking().FirstAsync();
        var snapshot = JsonSerializer.Deserialize<ApprovedExperiencePolicySnapshot>(original.SnapshotJson, ExperienceJson)!;
        var id = Guid.NewGuid();
        db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy
        {
            Id = id,
            BenchmarkId = original.BenchmarkId,
            SemesterId = original.SemesterId,
            Scope = "CUMULATIVE",
            Environment = "PROJECT",
            ApprovedBy = 900,
            ApprovalBasis = "TEST FIXTURE ONLY",
            ApprovedAt = DateTimeOffset.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(snapshot with { Scope = "CUMULATIVE", Policy = snapshot.Policy with { PolicyVersion = id } }, ExperienceJson)
        });
        await db.SaveChangesAsync();
    }

    private sealed class StudentSourceState
    {
        public bool Unavailable { get; set; }
        public HashSet<StudentSourceReference> CancelledSources { get; } = [];
        public Action<StudentCandidateQuery>? CandidateObserver { get; set; }
        public IReadOnlyList<StudentActivityItem> Activities { get; set; } =
        [new(1, 1, "C1", "Club 1", "Eligible activity", "Safe", null, DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(1),
            "Campus", "UPCOMING", "REGISTER", true, true, "FA26", 1, Guid.NewGuid(), Guid.NewGuid(), null, [new PillarWeight("Academic", 1)])];
    }
}

