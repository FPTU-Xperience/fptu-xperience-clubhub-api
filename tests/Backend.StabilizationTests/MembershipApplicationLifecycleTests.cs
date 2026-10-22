using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    private static object MembershipJoinPayload(string reason = "A new application") => new
    {
        fullName = "Untrusted browser name",
        email = "untrusted@example.invalid",
        gender = "OTHER",
        reason,
        acceptedClubRules = true,
        committedToParticipate = true
    };

    [Fact]
    public async Task MembershipLifecycle_ReapplicationCreatesNewIdAndPreservesRejectedAnswersAndDecision()
    {
        await using var app = await CreateMembershipLifecycleAppAsync();
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        int previousId;
        await using (var scope = app.Services.CreateAsyncScope())
            previousId = (await scope.ServiceProvider.GetRequiredService<ClubDbContext>().ClubMemberships.SingleAsync()).Id;
        var response = await student.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload());
        Assert.True(response.IsSuccessStatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(previousId, result.GetProperty("id").GetInt32());
        await using var verify = app.Services.CreateAsyncScope();
        var rows = await verify.ServiceProvider.GetRequiredService<ClubDbContext>().ClubMemberships.IgnoreQueryFilters().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Old application answers", rows[0].Reason);
        Assert.Equal("Original reviewer decision", rows[0].ReviewNote);
        Assert.Equal(ClubMembershipStatuses.Rejected, rows[0].Status);
        Assert.True(rows[0].IsDeleted);
        Assert.Equal("A new application", rows[1].Reason);
    }

    private static async Task<WebApplication> CreateMembershipLifecycleAppAsync(string? connection = null, bool targetLocked = false, string? targetCampus = null)
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
        var name = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ClubDbContext>(o => { if (connection is null) o.UseInMemoryDatabase(name); else o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()); });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new ClubReportHub.Shared.Rosters.SemesterRosterClient(
            new HttpClient(new CanonicalSemesterRosterHandler(104, "Canonical student")) { BaseAddress = new Uri("http://roster-fixture.invalid/") }, builder.Configuration));
        builder.Services.AddSingleton(new CurrentUserProfileClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new[] { new InternalUserProfile(104, "Canonical student", "student@example.invalid", targetCampus ?? CampusCodes.Hanoi, true, targetLocked, null) }) }), builder.Configuration));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapClubEndpoints(); app.MapMembershipEndpoints();
        app.MapSelfMembershipApplicationEndpoints(); app.MapMemberManagementEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            if (connection is not null) await db.Database.MigrateAsync();
            var club = new Club { Code = "ONE", Name = "Club One", IsRecruiting = true }; db.Clubs.Add(club); await db.SaveChangesAsync();
            db.ClubManagerAssignments.Add(new ClubManagerAssignment { ClubId = club.Id, ManagerUserId = 100, IsActive = true });
            db.ClubMemberships.Add(new ClubMembership
            {
                ClubId = club.Id,
                UserId = 104,
                FullName = "Canonical student",
                Reason = "Old application answers",
                ReviewNote = "Original reviewer decision",
                ReviewedByUserId = 100,
                ReviewedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                Status = ClubMembershipStatuses.Rejected,
                AcceptedClubRules = true,
                CommittedToParticipate = true
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }
}
