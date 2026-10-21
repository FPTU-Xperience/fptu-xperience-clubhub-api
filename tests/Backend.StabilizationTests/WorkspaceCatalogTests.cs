using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    private static readonly Guid WorkspaceSemesterId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task WorkspaceCatalog_ManagerReceivesCanonicalSafeSemesterAliasesAndApprovedEnvironmentPolicies()
    {
        await using var admin = await CreateWorkspaceAdminAppAsync(aliases: true);
        await using var app = await CreateWorkspaceClubAppAsync(admin);
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var response = await client.GetAsync("/api/clubs/1/workspace-semesters");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var semester = body.GetProperty("items")[0];
        Assert.Equal(WorkspaceSemesterId, semester.GetProperty("id").GetGuid());
        Assert.Equal("CUSTOM_PERIOD", semester.GetProperty("code").GetString());
        Assert.Equal(2026, semester.GetProperty("year").GetInt32());
        Assert.Equal("FA", semester.GetProperty("term").GetString());
        Assert.True(semester.GetProperty("isCurrent").GetBoolean());
        Assert.True(semester.GetProperty("editable").GetBoolean());
        Assert.False(semester.GetProperty("canInvite").GetBoolean());
        Assert.Equal("ROSTER_UNAVAILABLE", semester.GetProperty("invitationAvailability").GetString());
        Assert.Equal("2026-10", semester.GetProperty("reportPeriod").GetString());
        var invitations = await client.GetFromJsonAsync<JsonElement>("/api/clubs/1/invitation-semesters");
        Assert.Equal("CUSTOM_PERIOD", invitations.GetProperty("items")[0].GetProperty("semesterId").GetString());
        var policies = await client.GetFromJsonAsync<JsonElement>($"/api/clubs/1/experience-policies?semesterId={WorkspaceSemesterId}");
        Assert.Equal("CUSTOM_PERIOD", policies.GetProperty("semesterId").GetString());
        Assert.Single(policies.GetProperty("items").EnumerateArray());
        Assert.Equal("PROJECT", policies.GetProperty("items")[0].GetProperty("policy").GetProperty("environment").GetString());
        Assert.DoesNotContain("PRIVATE_APPROVAL_BASIS", policies.GetRawText());
        Assert.DoesNotContain("tau", policies.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approvedBy", policies.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceCatalog_UnknownAliasesAndAmbiguousCurrentAreExplicitAndHistoricalReadsDoNotGrantMutation()
    {
        await using var admin = await CreateWorkspaceAdminAppAsync();
        await using (var scope = admin.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.Semesters.Add(new Semester { SemesterCode = "ANOTHER_ACTIVE", IsActive = true }); await db.SaveChangesAsync();
        }
        await using var app = await CreateWorkspaceClubAppAsync(admin);
        using var member = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var catalog = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/workspace-semesters");
        Assert.Equal("AMBIGUOUS", catalog.GetProperty("currentAvailability").GetString());
        Assert.All(catalog.GetProperty("items").EnumerateArray(), x =>
        {
            Assert.False(x.GetProperty("isCurrent").GetBoolean()); Assert.False(x.GetProperty("editable").GetBoolean());
            Assert.Equal(JsonValueKind.Null, x.GetProperty("year").ValueKind);
            Assert.Equal(JsonValueKind.Null, x.GetProperty("reportPeriod").ValueKind);
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/clubs/1/invitation-semesters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/clubs/1/experience-policies?semesterId=CUSTOM_PERIOD")).StatusCode);
    }

    [Fact]
    public async Task WorkspaceCatalog_RequiresCurrentExactClubAuthorityAndInternalTokenAndPropagatesAuthorityFailure()
    {
        await using var admin = await CreateWorkspaceAdminAppAsync();
        using var direct = CreateAdminContractClient(admin, 100, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await direct.GetAsync("/api/internal/experience/workspace-catalog")).StatusCode);
        using var anonymous = admin.GetTestClient();
        anonymous.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", TestInternalWorkflowToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/internal/experience/workspace-catalog")).StatusCode);
        await using var app = await CreateWorkspaceClubAppAsync(admin);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var stranger = CreateAdminContractClient(app, 101, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/api/clubs/1/workspace-semesters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/clubs/2/workspace-semesters")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/clubs/1/experience-policies")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/clubs/1/experience-policies?semesterId=MISSING")).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            (await db.ClubManagerAssignments.SingleAsync()).IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/clubs/1/workspace-semesters")).StatusCode);
        await using var down = await CreateWorkspaceClubAppAsync(admin, unavailable: true);
        using var unavailable = CreateAdminContractClient(down, 100, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await unavailable.GetAsync("/api/clubs/1/workspace-semesters")).StatusCode);
    }

    private static async Task<WebApplication> CreateWorkspaceAdminAppAsync(bool aliases = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        var now = DateTimeOffset.UtcNow;
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        };
        if (aliases)
        {
            var prefix = $"Workspace:SemesterAliases:{WorkspaceSemesterId:D}:";
            settings[prefix + "Year"] = "2026"; settings[prefix + "Term"] = "FA"; settings[prefix + "ReportPeriod"] = "2026-10";
            settings[prefix + "EditableFromUtc"] = now.AddDays(-1).ToString("O"); settings[prefix + "EditableUntilUtc"] = now.AddDays(1).ToString("O");
        }
        builder.Configuration.AddInMemoryCollection(settings);
        var name = Guid.NewGuid().ToString(); builder.Services.AddDbContext<AdminDbContext>(o => o.UseInMemoryDatabase(name));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddAuthorization(o => o.AddPolicy(AdminPolicies.AnyActor, p => p.RequireAuthenticatedUser()));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapWorkspaceCatalogEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.Semesters.Add(new Semester
            {
                Id = WorkspaceSemesterId,
                SemesterCode = "CUSTOM_PERIOD",
                Label = "Configured period",
                IsActive = true,
                StartDate = now.AddMonths(-1),
                EndDate = now.AddMonths(1)
            });
            var source = ManagerSource() with { SemesterId = "CUSTOM_PERIOD", Policy = ManagerSource().Policy with { SemesterId = "CUSTOM_PERIOD" } };
            var benchmark = new SemesterBenchmarkConfig { SemesterCode = source.SemesterId, ApprovalStatus = "APPROVED" };
            db.SemesterBenchmarkConfigs.Add(benchmark);
            var original = new ApprovedExperiencePolicy
            {
                Id = source.Policy.PolicyVersion,
                BenchmarkId = benchmark.Id,
                SemesterId = source.SemesterId,
                Environment = "PROJECT",
                ApprovedAt = now.AddDays(-1),
                ApprovalBasis = "PRIVATE_APPROVAL_BASIS",
                SnapshotJson = JsonSerializer.Serialize(new AdminService.Contracts.ApprovedExperiencePolicySnapshot(
                    source.Policy, benchmark.Id, benchmark.ToTauDictionary(), 20, 40, 60, 30, 0.9m), ExperienceJson)
            };
            db.ApprovedExperiencePolicies.Add(original);
            db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy
            {
                BenchmarkId = original.BenchmarkId,
                SemesterId = "CUSTOM_PERIOD",
                Scope = "CUMULATIVE",
                ApprovedAt = now.AddDays(-1),
                Environment = "PROJECT",
                SnapshotJson = "{}"
            });
            db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy
            {
                BenchmarkId = original.BenchmarkId,
                SemesterId = "CUSTOM_PERIOD",
                Scope = "PERIOD",
                ApprovedAt = now.AddDays(-1),
                Environment = "DEMO",
                SnapshotJson = "{}"
            });
            db.ApprovedExperiencePolicies.Add(new ApprovedExperiencePolicy
            {
                BenchmarkId = original.BenchmarkId,
                SemesterId = "CUSTOM_PERIOD",
                Scope = "PERIOD",
                ApprovedAt = now.AddDays(-1),
                EffectiveAt = now.AddDays(1),
                Environment = "PROJECT",
                SnapshotJson = "{}"
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }

    private static async Task<WebApplication> CreateWorkspaceClubAppAsync(WebApplication admin, bool unavailable = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        });
        var name = Guid.NewGuid().ToString(); builder.Services.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(name));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new WorkspaceCatalogClient(unavailable
            ? CreateFakeHttpClient(_ => new(HttpStatusCode.ServiceUnavailable)) : admin.GetTestClient(), builder.Configuration));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapClubWorkspaceCatalogEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            db.Clubs.AddRange(new Club { Id = 1, Code = "ONE", CampusCode = CampusCodes.Hanoi }, new Club { Id = 2, Code = "TWO", CampusCode = CampusCodes.HoChiMinh });
            db.ClubManagerAssignments.Add(new ClubManagerAssignment { ClubId = 1, ManagerUserId = 100, IsActive = true });
            db.ClubMemberships.Add(new ClubMembership { ClubId = 1, UserId = 104, Status = ClubMembershipStatuses.Approved });
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }
}
