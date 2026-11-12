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
using Microsoft.Extensions.Options;

namespace Backend.StabilizationTests;
public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    internal static async Task<WebApplication> CreateFormationAppAsync(string? connection = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = SigningKey, ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken });
        var name = Guid.NewGuid().ToString(); builder.Services.AddDbContext<ClubDbContext>(o => { if (connection is null) o.UseInMemoryDatabase(name); else o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()); });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new CurrentUserProfileClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] {
            new InternalUserProfile(100,"Requester","requester@example.test","HCM",true,false,null),
            new InternalUserProfile(200,"Selected President","president@example.test","HCM",true,false,null),
            new InternalUserProfile(300,"Other Campus","other@example.test","HAN",true,false,null),
            new InternalUserProfile(400,"Locked","locked@example.test","HCM",true,true,null)
        })
        }), builder.Configuration));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapApplicationEndpoints(); app.MapClubPublicProfileEndpoints();
        await using (var scope = app.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); if (connection is not null) await db.Database.MigrateAsync(); }
        await app.StartAsync(); return app;
    }
    internal static HttpClient FormationClient(WebApplication app, int id, string role, string campus = "HCM")
    {
        var client = app.GetTestClient(); var factory = new JwtTokenFactory(Options.Create(new JwtOptions { Issuer = "ClubReportHub", Audience = "ClubReportHub.Client", SigningKey = SigningKey }));
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(id, $"user{id}", $"User {id}", [role], campusCode: campus).AccessToken); return client;
    }
    internal static object FormationBody(string code = "FORM") => new { code, name = "Formation Club", category = "TECHNOLOGY", purpose = "Learn together", description = "Build together", founderFullName = "Forged name", founderEmail = "fake@example.test", founderPhone = "0912345678", founderRole = "Founder", founderOrganization = "SE", foundingMemberCount = 1, foundingMembers = new[] { new { fullName = "Requester", email = "requester@example.test", organization = "SE" } }, foundingMembersCommitted = true, mainActivities = "Workshops", activityFrequency = "2 lần mỗi tháng", venueSupport = "SELF_MANAGED", fundingSupport = "SELF_FUNDED", committedToRules = true, committedToResponsibility = true, committedToReporting = true };
    [Fact]
    public async Task Formation_MinimalSubmitRevisionApprovalAndWorkspacePersistAcrossReload()
    {
        await using var app = await CreateFormationAppAsync(); using var student = FormationClient(app, 100, AuthRoles.ClubMember); using var reviewer = FormationClient(app, 900, AuthRoles.StudentAffairsAdmin);
        var application = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(FormationBody()))!;
        application["tagSelection"] = JsonSerializer.SerializeToElement(new { experience = new[] { "RealWorldWork" }, custom = new[] { "API design" } });
        student.DefaultRequestHeaders.Add("Idempotency-Key", "submit-one"); var response = await student.PostAsJsonAsync("/api/clubs/applications", application); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = row.GetProperty("id").GetInt32(); var version = row.GetProperty("version").GetGuid();
        Assert.Equal("HCM", row.GetProperty("campusCode").GetString()); Assert.Equal("Requester", row.GetProperty("requesterName").GetString()); Assert.Equal("requester@example.test", row.GetProperty("contactEmail").GetString());
        Assert.Equal(HttpStatusCode.Created, (await student.PostAsJsonAsync("/api/clubs/applications", application)).StatusCode);
        reviewer.DefaultRequestHeaders.Add("Idempotency-Key", "revise"); Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync($"/api/clubs/applications/{id}/request-revision", new { expectedVersion = version, note = "Clarify activities" })).StatusCode);
        row = await student.GetFromJsonAsync<JsonElement>($"/api/clubs/applications/{id}"); Assert.Equal("NeedsRevision", row.GetProperty("status").GetString());
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(FormationBody()))!; fields["expectedVersion"] = JsonSerializer.SerializeToElement(row.GetProperty("version").GetGuid());
        student.DefaultRequestHeaders.Remove("Idempotency-Key"); student.DefaultRequestHeaders.Add("Idempotency-Key", "resubmit");
        Assert.Equal(HttpStatusCode.OK, (await student.PutAsJsonAsync($"/api/clubs/applications/{id}", fields)).StatusCode);
        row = await reviewer.GetFromJsonAsync<JsonElement>($"/api/clubs/applications/{id}"); var review = new { expectedVersion = row.GetProperty("version").GetGuid(), managerUserId = 200, note = "Approved formation" };
        reviewer.DefaultRequestHeaders.Remove("Idempotency-Key"); reviewer.DefaultRequestHeaders.Add("Idempotency-Key", "approve"); response = await reviewer.PostAsJsonAsync($"/api/clubs/applications/{id}/approve", review); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync($"/api/clubs/applications/{id}/approve", review)).StatusCode);
        row = await student.GetFromJsonAsync<JsonElement>($"/api/clubs/applications/{id}"); Assert.Equal(200, row.GetProperty("approvedManagerUserId").GetInt32()); Assert.Equal("Selected President", row.GetProperty("approvedManagerName").GetString());
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); var club = await db.Clubs.SingleAsync(); Assert.Equal("FORM", club.Code); Assert.Equal("HCM", club.CampusCode);
        Assert.Contains(DiscoveryTags.ForClub(club.TagSelectionJson, club.Category), tag => tag.Id == "experience:RealWorldWork");
        Assert.Contains(DiscoveryTags.ForClub(club.TagSelectionJson, club.Category), tag => tag.Label == "API design");
        Assert.Equal(200, (await db.ClubManagerAssignments.SingleAsync()).ManagerUserId); Assert.Equal(2, await db.ClubMemberships.CountAsync()); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == "club.created"));
        using var manager = FormationClient(app, 200, AuthRoles.ClubMember); var patch = new
        {
            expectedVersion = club.ConcurrencyToken,
            expectedLocation = "Campus Hall",
            majorEvents = "Annual festival",
            equipmentNeeds = "Microphones",
            advisorNeeded = true,
            tagSelection = new { experience = new[] { "Research" }, custom = new[] { "API design" } }
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PatchAsJsonAsync($"/api/clubs/{club.Id}/public-profile", patch)).StatusCode);
        response = await manager.PatchAsJsonAsync($"/api/clubs/{club.Id}/public-profile", patch); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await db.Entry(club).ReloadAsync(); Assert.Equal("Campus Hall", club.ExpectedLocation); Assert.True(club.AdvisorNeeded); Assert.Equal("Microphones", club.EquipmentNeeds);
        Assert.Contains(DiscoveryTags.ForClub(club.TagSelectionJson, club.Category), tag => tag.Id == "experience:Research");
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PatchAsJsonAsync($"/api/clubs/{club.Id}/public-profile", patch)).StatusCode);
    }
    [Fact]
    public async Task Formation_CampusScopeInvalidLeaderAndStaleReviewFailClosed()
    {
        await using var app = await CreateFormationAppAsync(); using var student = FormationClient(app, 100, AuthRoles.ClubMember); student.DefaultRequestHeaders.Add("Idempotency-Key", "submit");
        var row = await (await student.PostAsJsonAsync("/api/clubs/applications", FormationBody())).Content.ReadFromJsonAsync<JsonElement>(); var id = row.GetProperty("id").GetInt32(); var version = row.GetProperty("version").GetGuid();
        using var wrong = FormationClient(app, 901, AuthRoles.StudentAffairsAdmin, "HAN"); Assert.Empty((await wrong.GetFromJsonAsync<JsonElement[]>("/api/clubs/applications"))!); Assert.Equal(HttpStatusCode.Forbidden, (await wrong.GetAsync($"/api/clubs/applications/{id}")).StatusCode);
        wrong.DefaultRequestHeaders.Add("Idempotency-Key", "wrong-campus"); Assert.Equal(HttpStatusCode.Forbidden, (await wrong.PostAsJsonAsync($"/api/clubs/applications/{id}/approve", new { expectedVersion = version, managerUserId = 200 })).StatusCode);
        using var reviewer = FormationClient(app, 900, AuthRoles.StudentAffairsAdmin); reviewer.DefaultRequestHeaders.Add("Idempotency-Key", "review");
        foreach (var target in new[] { 300, 400, 999 }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await reviewer.PostAsJsonAsync($"/api/clubs/applications/{id}/approve", new { expectedVersion = version, managerUserId = target })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PostAsJsonAsync($"/api/clubs/applications/{id}/approve", new { expectedVersion = Guid.NewGuid(), managerUserId = 200 })).StatusCode);
        using var other = FormationClient(app, 200, AuthRoles.ClubMember); Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/clubs/applications/{id}")).StatusCode);
        await using var scope = app.Services.CreateAsyncScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<ClubDbContext>().Clubs.ToListAsync());
    }
}
public sealed partial class ClubManagerSqlIntegrityTests
{
    [ClubManagerSqlFact]
    public async Task Formation_SqlConcurrentApprovalAndRejectionHaveOneTerminalOutcome()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync(); await using var app = await CrossServiceWorkflowAuthorizationTests.CreateFormationAppAsync(database.ConnectionString);
        using var student = CrossServiceWorkflowAuthorizationTests.FormationClient(app, 100, AuthRoles.ClubMember); student.DefaultRequestHeaders.Add("Idempotency-Key", "submit");
        var row = await (await student.PostAsJsonAsync("/api/clubs/applications", CrossServiceWorkflowAuthorizationTests.FormationBody())).Content.ReadFromJsonAsync<JsonElement>(); var id = row.GetProperty("id").GetInt32();
        using var approve = CrossServiceWorkflowAuthorizationTests.FormationClient(app, 900, AuthRoles.StudentAffairsAdmin); using var reject = CrossServiceWorkflowAuthorizationTests.FormationClient(app, 901, AuthRoles.StudentAffairsAdmin);
        approve.DefaultRequestHeaders.Add("Idempotency-Key", "approve"); reject.DefaultRequestHeaders.Add("Idempotency-Key", "reject"); var body = new { expectedVersion = row.GetProperty("version").GetGuid(), managerUserId = 200, note = "Concurrent review" };
        var responses = await Task.WhenAll(approve.PostAsJsonAsync($"/api/clubs/applications/{id}/approve", body), reject.PostAsJsonAsync($"/api/clubs/applications/{id}/reject", body));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); var saved = await db.ClubCreationApplications.SingleAsync(); Assert.Equal(saved.Status == "Approved" ? 1 : 0, await db.Clubs.CountAsync());
    }
}
