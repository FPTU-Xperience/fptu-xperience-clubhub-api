using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Data;
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
    [Fact]
    public async Task ClubManager_PublicProfilePreservesImagesAndStateAndUsesPersistedCategory()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Guid version;
        await using (var scope = app.Services.CreateAsyncScope())
            version = (await scope.ServiceProvider.GetRequiredService<ClubDbContext>().Clubs.FindAsync(1))!.ConcurrencyToken;
        var response = await client.PatchAsJsonAsync("/api/clubs/1/public-profile", new
        { description = " Changed ", category = "CUSTOM", scheduleLabel = (string?)null, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CUSTOM", body.GetProperty("category").GetString());
        Assert.NotEqual(version, body.GetProperty("version").GetGuid());
        await using var verify = app.Services.CreateAsyncScope();
        var club = await verify.ServiceProvider.GetRequiredService<ClubDbContext>().Clubs.FindAsync(1);
        Assert.Equal("Changed", club!.Description);
        Assert.Equal("Original", club.Name);
        Assert.Equal("https://images.example/logo.png", club.LogoUrl);
        Assert.Equal("https://images.example/cover.png", club.CoverImageUrl);
        Assert.Null(club.ScheduleLabel);
        Assert.True(club.IsActive);
        Assert.True(club.IsRecruiting);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync("/api/clubs/1/public-profile",
            new { name = "Lost update", expectedVersion = version })).StatusCode);
    }

    [Theory]
    [InlineData(101, AuthRoles.ClubManager)]
    [InlineData(102, AuthRoles.ClubManager)]
    [InlineData(104, AuthRoles.ClubMember)]
    public async Task ClubManager_PublicProfileRequiresCurrentExactClubOwnership(int actor, string role)
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, actor, role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync("/api/clubs/1/public-profile",
            new { name = "Unauthorized", expectedVersion = Guid.NewGuid() })).StatusCode);
    }

    [Theory]
    [InlineData("isActive", true)]
    [InlineData("managerUserId", 104)]
    [InlineData("logoUrl", "https://evil.example/x")]
    [InlineData("category", "MISSING")]
    [InlineData("name", "")]
    [InlineData("contactEmail", "invalid")]
    public async Task ClubManager_PublicProfileRejectsProtectedAndInvalidFields(string field, object value)
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var club = (await db.Clubs.FindAsync(1))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/clubs/1/public-profile",
            new Dictionary<string, object> { [field] = value, ["expectedVersion"] = club.ConcurrencyToken })).StatusCode);
        Assert.Equal("Original", (await db.Clubs.AsNoTracking().SingleAsync(x => x.Id == 1)).Name);
    }

    [Fact]
    public async Task ClubManager_SemesterPlanCanonicalizesCodeAndKeepsRevisionsAndIdempotentReceipt()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "plan-create");
        var request = new { title = "Semester plan", targetActivityCount = 3, targetSessionCount = 8, targetUniqueParticipantCount = 20 };
        var created = await client.PutAsJsonAsync("/api/clubs/1/semester-plans/FALL2026", request);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FALL2026", first.GetProperty("semesterId").GetString());
        var replay = await client.PutAsJsonAsync("/api/clubs/1/semester-plans/FALL2026", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first.GetProperty("version").GetGuid(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/clubs/1/semester-plans/FALL2026",
            new { title = "Another payload", targetActivityCount = 3, targetSessionCount = 8, targetUniqueParticipantCount = 20 })).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "plan-update");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/clubs/1/semester-plans/FALL2026", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/clubs/1/semester-plans/FALL2026", new
        {
            title = "Updated",
            targetActivityCount = 0,
            targetSessionCount = 0,
            targetUniqueParticipantCount = 0,
            expectedVersion = first.GetProperty("version").GetGuid()
        })).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        Assert.Equal(1, await db.SemesterPlans.CountAsync());
        Assert.Equal(2, await db.PlanRevisions.CountAsync());
        Assert.Equal(2, await db.Set<ApiOperationReceipt>().CountAsync());
        Assert.Equal(0, (await db.SemesterPlans.SingleAsync()).TargetUniqueParticipantCount);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/clubs/1/semester-plans/FAKE")).StatusCode);
    }

    [Fact]
    public async Task ClubManager_MembershipDecisionReplayAndVersionDoNotApplySecondDecision()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Guid version;
        await using (var scope = app.Services.CreateAsyncScope())
            version = (await scope.ServiceProvider.GetRequiredService<ClubDbContext>().ClubMemberships.FindAsync(10))!.ConcurrencyToken;
        client.DefaultRequestHeaders.Add("Idempotency-Key", "approve");
        var request = new { note = "Accepted", expectedVersion = version };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/clubs/memberships/10/approve", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/clubs/memberships/10/approve", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/clubs/memberships/10/reject",
            new { note = "Rejected", expectedVersion = version })).StatusCode);
        await using var verify = app.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ClubDbContext>();
        Assert.Equal(ClubMembershipStatuses.Approved, (await db.ClubMemberships.FindAsync(10))!.Status);
        Assert.Equal(1, await db.OutboxMessages.CountAsync());
        Assert.Equal(1, await db.Set<ApiOperationReceipt>().CountAsync());
        (await db.ClubManagerAssignments.SingleAsync(x => x.ManagerUserId == 100)).IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/clubs/memberships/10/approve", request)).StatusCode);
    }

    private static async Task<WebApplication> CreateClubManagerAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        });
        var name = "club-manager-" + Guid.NewGuid();
        builder.Services.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(name));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new SemesterCatalogClient(CreateFakeHttpClient(request =>
            request.RequestUri!.AbsolutePath.EndsWith("FAKE", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new SemesterReference(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"), "FALL2026", DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(2), true))
                }), builder.Configuration));
        builder.Services.AddSingleton(new MediaAttachmentClient(CreateFakeHttpClient(request =>
        {
            var body = JsonSerializer.Deserialize<JsonElement>(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var url = body.GetProperty("publicUrl").GetString();
            return new HttpResponseMessage(url?.StartsWith("https://images.example/", StringComparison.Ordinal) == true
                ? HttpStatusCode.OK : HttpStatusCode.Conflict)
            { Content = JsonContent.Create(new { publicUrl = url }) };
        }), builder.Configuration));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapClubEndpoints();
        app.MapMembershipEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            db.Clubs.Add(new Club
            {
                Id = 1,
                Code = "ONE",
                Name = "Original",
                Description = "Original",
                IsRecruiting = true,
                LogoUrl = "https://images.example/logo.png",
                CoverImageUrl = "https://images.example/cover.png",
                ScheduleLabel = "Old",
                ScheduleJson = "{\"semesterId\":\"FALL2026\",\"weekdays\":[1]}"
            });
            db.ClubCategories.Add(new ClubCategory { Code = "CUSTOM", Name = "Custom category" });
            db.ClubManagerAssignments.AddRange(new ClubManagerAssignment { ClubId = 1, ManagerUserId = 100, IsActive = true },
                new ClubManagerAssignment { ClubId = 1, ManagerUserId = 102, IsActive = false });
            db.ClubMemberships.Add(new ClubMembership
            {
                Id = 10,
                ClubId = 1,
                UserId = 104,
                FullName = "Student",
                Status = ClubMembershipStatuses.Pending,
                AcceptedClubRules = true,
                CommittedToParticipate = true
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        return app;
    }
}
