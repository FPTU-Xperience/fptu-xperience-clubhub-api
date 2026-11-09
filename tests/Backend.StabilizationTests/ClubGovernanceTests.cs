using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using ClubService.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.StabilizationTests;

public sealed class GovernanceSqlFactAttribute : FactAttribute
{
    public GovernanceSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")))
            Skip = "SQL Server test connection is not configured; full CI supplies it.";
    }
}

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    private sealed class GovernanceState
    {
        internal static readonly Guid SemesterId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        internal bool Current = true;
        internal Dictionary<int, InternalUserProfile> Accounts = new[] { 100, 104, 105, 106, 200, 201, 300 }
            .ToDictionary(id => id, id => new InternalUserProfile(id, "Account " + id, id + "@example.invalid", id == 201 ? "HCM" : "HAN", true, false, null));
        internal WorkspaceCatalog Catalog => new([new(SemesterId, "TEST-SEMESTER", "Test semester", Current, Current,
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(20), "SEMESTER", 2026, "TEST", "AVAILABLE", "AVAILABLE")], [], "TEST", "AVAILABLE", DateTimeOffset.UtcNow);
    }
    private static async Task<WebApplication> GovernanceApp(GovernanceState state, string? connection = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey,
            ["Security:InternalWorkflowToken"] = TestInternalWorkflowToken
        });
        var name = Guid.NewGuid().ToString(); builder.Services.AddDbContext<ClubDbContext>(o => { if (connection is null) o.UseInMemoryDatabase(name); else o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()); });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(new CurrentUserProfileClient(CreateFakeHttpClient(request =>
        {
            var ids = request.Content!.ReadFromJsonAsync<int[]>().GetAwaiter().GetResult()!;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(ids.Where(state.Accounts.ContainsKey).Select(id => state.Accounts[id]).ToArray()) };
        }), builder.Configuration));
        builder.Services.AddSingleton(new WorkspaceCatalogClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(state.Catalog) }), builder.Configuration));
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapClubEndpoints(); app.MapClubRoleEndpoints(); app.MapMembershipEndpoints(); app.MapMemberManagementEndpoints(); app.MapTransferEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); if (connection is not null) await db.Database.MigrateAsync();
            var club = new Club { Code = "GOVERNANCE", Name = "Governance test", CampusCode = "HAN" };
            club.ManagerAssignments.Add(new ClubManagerAssignment { ManagerUserId = 100, ManagerName = "Owner" });
            foreach (var id in new[] { 100, 104, 105, 106 }) club.Memberships.Add(new ClubMembership { UserId = id, FullName = "Member " + id, Email = id + "@example.invalid", Status = ClubMembershipStatuses.Approved });
            db.Add(club); await db.SaveChangesAsync();
        }
        await app.StartAsync(); return app;
    }
    private static string GovernanceUrl => $"/api/clubs/1/governance/terms/{GovernanceState.SemesterId}";
    private static async Task<JsonElement> GovernanceWrite(HttpClient client, string path, object input, HttpStatusCode expected = HttpStatusCode.OK, string method = "POST")
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(new HttpRequestMessage(new(method), path) { Content = JsonContent.Create(input) });
        var content = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"{response.StatusCode}: {content}");
        return string.IsNullOrEmpty(content) ? default : JsonSerializer.Deserialize<JsonElement>(content);
    }
    private static async Task<JsonElement> InitializeGovernance(HttpClient owner)
    {
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        return await GovernanceWrite(owner, GovernanceUrl + "/initialize", new GovernanceInitializeInput(term.GetProperty("clubVersion").GetGuid(), "Initialize real test period"));
    }

    [Fact]
    public async Task Governance_GroupLeadManagesOnlyOwnAssignmentsAndCannotChangePermissions()
    {
        await using var app = await GovernanceApp(new GovernanceState());
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN");
        using var lead = FormationClient(app, 104, AuthRoles.ClubMember, "HAN");
        await InitializeGovernance(owner);
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var groups = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().ToArray();
        var group = groups.Single(x => x.GetProperty("presetCode").GetString() == "EVENT");
        var groupId = group.GetProperty("id").GetGuid();
        var otherGroup = groups.Single(x => x.GetProperty("presetCode").GetString() == "CONTENT").GetProperty("id").GetGuid();
        var permissions = group.GetProperty("permissionCodes").GetRawText();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var memberships = await db.ClubMemberships.ToDictionaryAsync(x => x.UserId, x => x.Id);
        var path = GovernanceUrl + "/assignment-changes";
        GovernanceAssignmentInput Input(string operation, Guid target, int userId, int? replacement = null) =>
            new(term.GetProperty("version").GetGuid(), "Verify group lead authority", operation, target, [memberships[userId]], ReplaceMembershipId: replacement);

        term = await GovernanceWrite(owner, path, Input("ADD", groupId, 104));
        await GovernanceWrite(lead, path, Input("ADD", groupId, 105), HttpStatusCode.Forbidden);
        term = await GovernanceWrite(owner, path, Input("SET_LEAD", groupId, 104));
        var view = await lead.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        Assert.False(view.GetProperty("capabilities").GetProperty("canConfigure").GetBoolean());
        Assert.Equal(groupId, Assert.Single(view.GetProperty("capabilities").GetProperty("manageableGroupIds").EnumerateArray()).GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await lead.GetAsync($"{GovernanceUrl}/eligible-members?groupId={groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lead.GetAsync($"{GovernanceUrl}/eligible-members?groupId={otherGroup}")).StatusCode);
        await GovernanceWrite(lead, GovernanceUrl + $"/groups/{groupId}", new GovernanceGroupInput(term.GetProperty("version").GetGuid(), "Reject permission change", PermissionCodes: []), HttpStatusCode.Forbidden, "PATCH");
        await GovernanceWrite(lead, path, Input("ADD", otherGroup, 105), HttpStatusCode.Forbidden);
        await GovernanceWrite(lead, path, Input("REMOVE", groupId, 104), HttpStatusCode.Forbidden);
        term = await GovernanceWrite(lead, path, Input("ADD", groupId, 105));
        await GovernanceWrite(lead, path, Input("SET_LEAD", groupId, 105), HttpStatusCode.Forbidden);
        term = await GovernanceWrite(lead, path, Input("REPLACE", groupId, 106, memberships[105]));
        var updated = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == groupId);
        Assert.Equal(permissions, updated.GetProperty("permissionCodes").GetRawText());
        Assert.Equal(new[] { 104, 106 }, updated.GetProperty("assignments").EnumerateArray().Select(x => x.GetProperty("userId").GetInt32()));
        term = await GovernanceWrite(lead, path, Input("REMOVE", groupId, 106));
        term = await GovernanceWrite(owner, path, Input("CLEAR_LEAD", groupId, 104));
        await GovernanceWrite(lead, path, Input("ADD", groupId, 105), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Governance_MultipleGroupsUnionAndImmutableReportDoNotActivateOrRetainRevokedPermissions()
    {
        var state = new GovernanceState(); await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN"); using var member = FormationClient(app, 104, AuthRoles.ClubMember, "HAN");
        await InitializeGovernance(owner);
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl); var groups = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(7, groups.Length);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var membership = await db.ClubMemberships.SingleAsync(x => x.UserId == 104);
        foreach (var preset in new[] { "CONTENT", "HR" })
        {
            term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl); var group = groups.Single(x => x.GetProperty("presetCode").GetString() == preset);
            await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Assign specialist group", "ADD", group.GetProperty("id").GetGuid(), [membership.Id]));
        }
        var access = (await member.GetFromJsonAsync<JsonElement[]>("/api/clubs/me/access"))!.Single();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var delegated = new ClubAccessClient(member, cache, NullLogger<ClubAccessClient>.Instance);
        Assert.True((await delegated.GetMyAccessAsync(member.DefaultRequestHeaders.Authorization!.Parameter!)).Single().HasCapability("event.content.write"));
        Assert.False(access.GetProperty("isManager").GetBoolean()); Assert.Equal(2, access.GetProperty("groupIds").GetArrayLength());
        Assert.Equal(1, access.GetProperty("capabilities").EnumerateArray().Count(x => x.GetString() == "club.profile.write"));
        Assert.Contains("club.members.recruit", access.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        await GovernanceWrite(member, "/api/clubs/1/public-profile", new { expectedVersion = term.GetProperty("clubVersion").GetGuid(), description = "Assigned group can edit club text" }, method: "PATCH");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/clubs/1/memberships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/clubs/1/members?page=1&pageSize=20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync("/api/clubs/1/members/3")).StatusCode);
        term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var report = await GovernanceWrite(owner, GovernanceUrl + "/report-submissions", new GovernanceReportInput(term.GetProperty("version").GetGuid(), "Submit semester board snapshot", term.GetProperty("revisionId").GetGuid(), term.GetProperty("leadershipDigest").GetString()!, term.GetProperty("clubVersion").GetGuid()), HttpStatusCode.Created);
        var frozen = await owner.GetStringAsync(GovernanceUrl + "/report-submissions/" + report.GetProperty("id").GetGuid());
        var contentGroup = groups.Single(x => x.GetProperty("presetCode").GetString() == "CONTENT");
        await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Remove content duty", "REMOVE", contentGroup.GetProperty("id").GetGuid(), [membership.Id]));
        access = (await member.GetFromJsonAsync<JsonElement[]>("/api/clubs/me/access"))!.Single();
        Assert.Contains("club.profile.write", access.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("event.content.write", access.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        Assert.False((await delegated.GetMyAccessAsync(member.DefaultRequestHeaders.Authorization!.Parameter!)).Single().HasCapability("event.content.write"));
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/clubs/1/governance/history?membershipId={membership.Id}&page=1&pageSize=2");
        Assert.Equal(3, history.GetProperty("totalItems").GetInt32()); Assert.Equal(2, history.GetProperty("items").GetArrayLength());
        Assert.Equal(frozen, await owner.GetStringAsync(GovernanceUrl + "/report-submissions/" + report.GetProperty("id").GetGuid()));
        term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl); Assert.True(term.GetProperty("reportNeedsUpdate").GetBoolean());
        state.Current = false;
        access = (await member.GetFromJsonAsync<JsonElement[]>("/api/clubs/me/access"))!.Single();
        Assert.False(access.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array && capabilities.GetArrayLength() > 0);
        await GovernanceWrite(owner, GovernanceUrl + "/groups", new GovernanceGroupInput(term.GetProperty("version").GetGuid(), "Must not extend expired term", "Expired write"), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Governance_ProtectedLeadershipRequiresIndependentCurrentCampusApprovalAndFutureAppointmentDoesNotGrantEarly()
    {
        var state = new GovernanceState(); await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN"); using var reviewer = FormationClient(app, 200, AuthRoles.StudentAffairsAdmin, "HAN");
        using var wrongCampus = FormationClient(app, 201, AuthRoles.StudentAffairsAdmin, "HAN"); using var member = FormationClient(app, 104, AuthRoles.ClubMember, "HAN");
        await InitializeGovernance(owner); var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var protectedGroup = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().First(x => x.GetProperty("protected").GetBoolean());
        await GovernanceWrite(owner, GovernanceUrl + "/groups/" + protectedGroup.GetProperty("id").GetGuid(), new GovernanceGroupInput(term.GetProperty("version").GetGuid(), "Must not rewrite leadership", "Fake owner"), HttpStatusCode.Forbidden, "PATCH");
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); var membership = await db.ClubMemberships.SingleAsync(x => x.UserId == 104);
        var nomination = await GovernanceWrite(owner, "/api/clubs/1/governance/leadership-nominations", new ClubViceNominationInput(membership.Id, term.GetProperty("clubVersion").GetGuid(), "Propose vice leadership"), HttpStatusCode.Created);
        var path = "/api/clubs/1/governance/leadership-nominations/" + nomination.GetProperty("id").GetGuid();
        var decision = new ClubViceDecisionInput(nomination.GetProperty("version").GetGuid(), "Verify leadership", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(10));
        await GovernanceWrite(wrongCampus, path + "/approve", decision, HttpStatusCode.Forbidden);
        var approved = await GovernanceWrite(reviewer, path + "/approve", decision);
        var access = (await member.GetFromJsonAsync<JsonElement[]>("/api/clubs/me/access"))!.Single(); Assert.False(access.GetProperty("isVicePresident").GetBoolean());
        await GovernanceWrite(reviewer, path + "/revoke", new ClubViceDecisionInput(approved.GetProperty("version").GetGuid(), "Revoke future appointment"));
        Assert.Equal(3, await db.Set<ClubGovernanceLeadershipEvent>().CountAsync());
        Assert.True((await db.ClubManagerAssignments.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Governance_TreasurerLimitIncludesLegacyDutyAndOldWritersCannotBypassCurrentGroupAssignments()
    {
        var state = new GovernanceState(); await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN");
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles");
        var legacy = roles.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("userId").GetInt32() == 104);
        await GovernanceWrite(owner, "/api/clubs/1/roles/" + legacy.GetProperty("memberId").GetInt32(), new { role = "TREASURER", expectedVersion = legacy.GetProperty("version").GetGuid(), expectedClubVersion = roles.GetProperty("version").GetGuid(), reason = "Preserve explicit legacy duty" }, method: "PATCH");
        await InitializeGovernance(owner);
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var group = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().Single(x => x.GetProperty("presetCode").GetString() == "TREASURER").GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var second = await db.ClubMemberships.SingleAsync(x => x.UserId == 105); var third = await db.ClubMemberships.SingleAsync(x => x.UserId == 106);
        await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Assign second unique duty holder", "ADD", group, [second.Id]));
        term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Must reject third duty holder", "ADD", group, [third.Id]), HttpStatusCode.Conflict);
        await GovernanceWrite(owner, "/api/clubs/1/treasurers", new AssignTreasurerRequest(106, "Must not bypass groups"), HttpStatusCode.Conflict);
        roles = await owner.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles"); Assert.Equal(2, roles.GetProperty("treasurerCount").GetInt32());
        Assert.All(roles.GetProperty("items").EnumerateArray(), x => Assert.False(x.GetProperty("canAssignTreasurer").GetBoolean()));
        legacy = roles.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("userId").GetInt32() == 104);
        await GovernanceWrite(owner, "/api/clubs/1/roles/" + legacy.GetProperty("memberId").GetInt32(), new { role = "MEMBER", expectedVersion = legacy.GetProperty("version").GetGuid(), expectedClubVersion = roles.GetProperty("version").GetGuid(), reason = "Revoke legacy duty independently" }, method: "PATCH");
        term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        await GovernanceWrite(owner, GovernanceUrl + "/assignment-changes", new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Use released duty slot", "ADD", group, [third.Id]));
        roles = await owner.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles"); Assert.Equal(2, roles.GetProperty("treasurerCount").GetInt32());
        term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        await GovernanceWrite(owner, GovernanceUrl + "/groups", new GovernanceGroupInput(term.GetProperty("version").GetGuid(), "No self delegation", "Fake leadership", PermissionCodes: ["club.governance.write"]), HttpStatusCode.BadRequest);
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/clubs/1/governance/history?membershipId={legacy.GetProperty("memberId").GetInt32()}");
        Assert.Equal(2, history.GetProperty("totalItems").GetInt32());
        var revision = await db.Set<ClubGovernanceRevision>().FirstAsync(); revision.Reason = "Tamper";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Governance_ApprovedViceHasScopedLeadershipWithoutOwnerIdentityAndCannotBeRemovedThroughMembershipBypass()
    {
        var state = new GovernanceState(); await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN"); using var reviewer = FormationClient(app, 200, AuthRoles.StudentAffairsAdmin, "HAN");
        using var vice = FormationClient(app, 104, AuthRoles.ClubMember, "HAN"); await InitializeGovernance(owner);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var candidate = await db.ClubMemberships.SingleAsync(x => x.UserId == 104);
        var term = await owner.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var nomination = await GovernanceWrite(owner, "/api/clubs/1/governance/leadership-nominations", new ClubViceNominationInput(candidate.Id, term.GetProperty("clubVersion").GetGuid(), "Propose protected leadership"), HttpStatusCode.Created);
        var approved = await GovernanceWrite(reviewer, "/api/clubs/1/governance/leadership-nominations/" + nomination.GetProperty("id").GetGuid() + "/approve",
            new ClubViceDecisionInput(nomination.GetProperty("version").GetGuid(), "Independent appointment", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(10)));
        var access = (await vice.GetFromJsonAsync<JsonElement[]>("/api/clubs/me/access"))!.Single();
        Assert.True(access.GetProperty("isVicePresident").GetBoolean()); Assert.False(access.GetProperty("isManager").GetBoolean()); Assert.False(access.GetProperty("isTreasurer").GetBoolean());
        Assert.DoesNotContain(104, access.GetProperty("treasurerUserIds").EnumerateArray().Select(x => x.GetInt32()));
        term = await vice.GetFromJsonAsync<JsonElement>(GovernanceUrl); Assert.True(term.GetProperty("capabilities").GetProperty("canConfigure").GetBoolean());
        await GovernanceWrite(vice, GovernanceUrl + "/groups", new GovernanceGroupInput(term.GetProperty("version").GetGuid(), "Configure ordinary specialist group", "Vice-configured group"));
        owner.DefaultRequestHeaders.Remove("Idempotency-Key"); owner.DefaultRequestHeaders.Add("Idempotency-Key", "no-membership-bypass");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/clubs/1/members/{candidate.Id}")).StatusCode);
        var transfer = await vice.PostAsJsonAsync("/api/clubs/1/transfer-ownership", new { newOwnerUserId = 105, reason = "Not the owner" });
        Assert.Equal(HttpStatusCode.Forbidden, transfer.StatusCode);
        await GovernanceWrite(reviewer, "/api/clubs/1/governance/leadership-nominations/" + nomination.GetProperty("id").GetGuid() + "/revoke", new ClubViceDecisionInput(approved.GetProperty("version").GetGuid(), "End scoped authority"));
        term = await vice.GetFromJsonAsync<JsonElement>(GovernanceUrl); Assert.False(term.GetProperty("capabilities").GetProperty("canConfigure").GetBoolean());
    }

    [GovernanceSqlFact]
    public async Task Governance_SqlConcurrentGroupsShareLegacyTreasurerLimitAndHistoryUsesRealServerPagination()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync(); var state = new GovernanceState(); await using var app = await GovernanceApp(state, database.ConnectionString);
        using var first = FormationClient(app, 100, AuthRoles.ClubMember, "HAN"); using var second = FormationClient(app, 100, AuthRoles.ClubMember, "HAN");
        var roles = await first.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles"); var legacy = roles.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("userId").GetInt32() == 104);
        await GovernanceWrite(first, "/api/clubs/1/roles/" + legacy.GetProperty("memberId").GetInt32(), new { role = "TREASURER", expectedVersion = legacy.GetProperty("version").GetGuid(), expectedClubVersion = roles.GetProperty("version").GetGuid(), reason = "Existing legacy duty" }, method: "PATCH");
        await InitializeGovernance(first); var term = await first.GetFromJsonAsync<JsonElement>(GovernanceUrl);
        var group = term.GetProperty("configuration").GetProperty("groups").EnumerateArray().Single(x => x.GetProperty("presetCode").GetString() == "TREASURER").GetProperty("id").GetGuid();
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var members = await db.ClubMemberships.Where(x => x.UserId == 105 || x.UserId == 106).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        first.DefaultRequestHeaders.Remove("Idempotency-Key"); first.DefaultRequestHeaders.Add("Idempotency-Key", "governance-race-one"); second.DefaultRequestHeaders.Add("Idempotency-Key", "governance-race-two");
        var a = new GovernanceAssignmentInput(term.GetProperty("version").GetGuid(), "Concurrent second duty holder", "ADD", group, [members[0]]);
        var responses = await Task.WhenAll(first.PostAsJsonAsync(GovernanceUrl + "/assignment-changes", a), second.PostAsJsonAsync(GovernanceUrl + "/assignment-changes", a with { MembershipIds = [members[1]] }));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        roles = await first.GetFromJsonAsync<JsonElement>("/api/clubs/1/roles"); Assert.Equal(2, roles.GetProperty("treasurerCount").GetInt32());
        var history = await first.GetFromJsonAsync<JsonElement>("/api/clubs/1/governance/history?page=1&pageSize=1");
        Assert.Equal(3, history.GetProperty("totalItems").GetInt32()); Assert.Equal(1, history.GetProperty("items").GetArrayLength());
    }
}
