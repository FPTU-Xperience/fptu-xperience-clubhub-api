using AdminService.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AdminService.IntegrationTests;

public sealed class AdminApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) => Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(config,
            new Dictionary<string, string?> { ["Security:InternalWorkflowToken"] = "STUDENT-API-TEST-ONLY-INTERNAL-TOKEN" }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ClubReportHub.Shared.Experience.CurrentUserProfileClient>(provider => new(
                new HttpClient(new StudentFixtureHttpHandler()) { BaseAddress = new Uri("http://student-fixture.invalid/") },
                provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()));
            services.AddHttpClient("ExperienceActivity").ConfigurePrimaryHttpMessageHandler(() => new StudentFixtureHttpHandler());
            services.RemoveAll<DbContextOptions<AdminDbContext>>();
            services.RemoveAll<AdminDbContext>();
            _connection.Open();
            services.AddDbContext<AdminDbContext>(options => options.UseSqlite(_connection));

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AdminDbContext>().Database.EnsureCreated();
        return host;
    }

    public int CountAuditEvents()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AdminDbContext>().AuditRecords.Count();
    }

    public string? GetLatestAuditActorSubjectId()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AdminDbContext>()
            .AuditRecords
            .OrderByDescending(record => record.TimestampUtc)
            .Select(record => record.ActorSubjectId)
            .FirstOrDefault();
    }

    public Guid StudentPolicyVersion { get; } = Guid.NewGuid();

    public async Task EnsureStudentPolicyAsync()
    {
        await using var scope = Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        if (await db.ApprovedExperiencePolicies.AnyAsync(x => x.Id == StudentPolicyVersion)) return;
        var semester = await db.Semesters.SingleOrDefaultAsync(x => x.SemesterCode == "FA26");
        if (semester is null)
        {
            semester = new Semester
            {
                SemesterCode = "FA26",
                IsActive = true,
                StartDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EndDate = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)
            }; db.Semesters.Add(semester);
        }
        var benchmark = await db.SemesterBenchmarkConfigs.FirstOrDefaultAsync();
        if (benchmark is null) { benchmark = new SemesterBenchmarkConfig { SemesterCode = "FA26" }; db.SemesterBenchmarkConfigs.Add(benchmark); }
        var reference = new ClubReportHub.Shared.Experience.ExperiencePolicyReference(StudentPolicyVersion, "P_B_R_Q_K_V1", "FA26",
            semester.StartDate, semester.EndDate, "APPROVED", "PROJECT", 4, "REVERSE_SOURCE_INVALIDATION");
        db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy
        {
            Id = StudentPolicyVersion,
            BenchmarkId = benchmark.Id,
            SemesterId = "FA26",
            ApprovedAt = DateTimeOffset.UtcNow,
            ApprovalBasis = "TEST FIXTURE ONLY",
            ApprovedBy = 101,
            SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new AdminService.Contracts.ApprovedExperiencePolicySnapshot(reference,
                benchmark.Id, benchmark.ToTauDictionary(), 20, 50, 80, 20, 0.9m), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        });
        await db.SaveChangesAsync();
    }

    public async Task SeedStudentQuestProofAsync()
    {
        await EnsureStudentPolicyAsync();
        await using var scope = Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var attributes = new ClubReportHub.Shared.Experience.ContributionAttributes("T3", "Participant", "Club", "None",
            [new("Academic", 1)]);
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var row = new VerifiedContribution
        {
            UserId = 301,
            ActivityId = 1,
            SessionId = 1,
            SourceKey = "CLUB:1:SESSION:1",
            SemesterId = "FA26",
            Status = "APPROVED",
            RawPoints = 60,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProposedAttributesJson = System.Text.Json.JsonSerializer.Serialize(attributes, options),
            EffectiveAttributesJson = System.Text.Json.JsonSerializer.Serialize(attributes, options),
            PolicySnapshotJson = (await db.ApprovedExperiencePolicies.SingleAsync(x => x.Id == StudentPolicyVersion)).SnapshotJson
        };
        var revision = new ContributionRevision { ContributionId = row.Id, Ordinal = 1, AttributesJson = row.EffectiveAttributesJson, CreatedAtUtc = DateTimeOffset.UtcNow };
        row.CurrentRevisionId = revision.Id; db.Contributions.Add(row); db.ContributionRevisions.Add(revision);
        var effect = new ExperienceLedgerEffect
        {
            ContributionId = row.Id,
            UserId = 301,
            RevisionId = revision.Id,
            SemesterId = "FA26",
            Amount = 60,
            EffectType = "APPROVED",
            PolicyVersion = StudentPolicyVersion,
            FormulaVersion = "P_B_R_Q_K_V1",
            RecordedAt = DateTimeOffset.UtcNow
        };
        db.ExperienceLedgerEffects.Add(effect); await db.SaveChangesAsync();
        db.ExperienceLedgerAllocations.Add(new ExperienceLedgerAllocation { EffectId = effect.Id, Pillar = "Academic", Amount = 60 });
        await db.SaveChangesAsync();
    }

    private sealed class StudentFixtureHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object response;
            if (request.RequestUri!.AbsolutePath.EndsWith("/user-profiles", StringComparison.Ordinal))
                response = new[] { new ClubReportHub.Shared.Experience.InternalUserProfile(301, "Student One", "student1@fpt.edu.vn",
                    ClubReportHub.Shared.Auth.CampusCodes.Hanoi, true, false, null),
                    new ClubReportHub.Shared.Experience.InternalUserProfile(302, "Student Two", "student2@fpt.edu.vn",
                        ClubReportHub.Shared.Auth.CampusCodes.HoChiMinh, true, false, null) };
            else if (request.RequestUri.AbsolutePath.EndsWith("/source-status", StringComparison.Ordinal))
            {
                var sources = System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<ClubReportHub.Shared.Experience.StudentSourceStatusQuery>(request.Content!, cancellationToken)
                    .GetAwaiter().GetResult()!;
                response = new ClubReportHub.Shared.Experience.StudentSourceStatus(sources.Sources, DateTimeOffset.UtcNow);
            }
            else response = new ClubReportHub.Shared.Experience.StudentActivityPage(
                [new(1, 1, "C1", "Club 1", "Published activity", "Safe", null, DateTimeOffset.UtcNow.AddDays(1),
                    DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "Campus", "UPCOMING", "REGISTER", true, true, "FA26",
                    1, Guid.NewGuid(), Guid.NewGuid(), null, [new("Academic", 1)])],
                1, 100, 1, 1, DateTimeOffset.UtcNow, "TEST_SCOPE_ONLY");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(response) });
        }
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
