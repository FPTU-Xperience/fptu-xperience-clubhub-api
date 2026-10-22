using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task MembershipLifecycle_SelfWithdrawalIsIdempotentAuditedAndRetainsHistoryAcrossReapplication()
    {
        await using var app = await CreateMembershipLifecycleAppAsync();
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var created = await student.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload());
        var application = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = application.GetProperty("id").GetInt32();
        Assert.Equal("APPLICATION", application.GetProperty("source").GetString());
        Assert.Equal("ONE", application.GetProperty("clubCode").GetString());
        Assert.Equal("Canonical student", application.GetProperty("fullName").GetString());
        Assert.True(application.GetProperty("canWithdraw").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, application.GetProperty("consentedAt").ValueKind);
        var own = await student.GetFromJsonAsync<JsonElement>("/api/clubs/me/memberships");
        Assert.True(own[0].GetProperty("canWithdraw").GetBoolean());
        student.DefaultRequestHeaders.Add("Idempotency-Key", "withdraw-once");
        var url = $"/api/clubs/me/membership-applications/{id}/withdraw";
        var first = await student.PostAsync(url, null); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadAsStringAsync();
        var replay = await student.PostAsync(url, null); Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        student.DefaultRequestHeaders.Remove("Idempotency-Key");
        var noKeyReplay = await student.PostAsync(url, null); Assert.Equal(body, await noKeyReplay.Content.ReadAsStringAsync());
        var withdrawn = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("WITHDRAWN", withdrawn.GetProperty("status").GetString()); Assert.False(withdrawn.GetProperty("canWithdraw").GetBoolean());
        var newAttempt = await student.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload("New attempt after withdrawal"));
        Assert.Equal(HttpStatusCode.Created, newAttempt.StatusCode);
        var next = await newAttempt.Content.ReadFromJsonAsync<JsonElement>(); Assert.NotEqual(id, next.GetProperty("id").GetInt32());
        var history = await student.GetFromJsonAsync<JsonElement>("/api/clubs/me/membership-applications");
        Assert.Equal(3, history.GetArrayLength());
        Assert.Single(history.EnumerateArray(), x => x.GetProperty("status").GetString() == "WITHDRAWN");
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var old = await db.ClubMemberships.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        Assert.Equal("A new application", old.Reason); Assert.Equal(104, old.WithdrawnByUserId); Assert.True(old.IsDeleted);
        var audit = await db.MembershipActions.SingleAsync(); Assert.Equal(id, audit.MembershipId); Assert.Equal(104, audit.ActorUserId);
        Assert.Equal(application.GetProperty("version").GetGuid(), audit.PreviousVersion);
        Assert.Equal(withdrawn.GetProperty("version").GetGuid(), audit.Version);
        Assert.Equal(id, (await db.ClubMemberships.SingleAsync()).PreviousApplicationId);
    }

    [Fact]
    public async Task MembershipLifecycle_OnlyOwnerCanWithdrawAndAnotherAccountCannotReadHistoryOrCapabilities()
    {
        await using var app = await CreateMembershipLifecycleAppAsync();
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var create = await student.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload());
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using var other = CreateAdminContractClient(app, 105, AuthRoles.ClubMember);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        foreach (var client in new[] { other, manager })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/clubs/me/membership-applications/{id}/withdraw", null)).StatusCode);
            var history = await client.GetFromJsonAsync<JsonElement>("/api/clubs/me/membership-applications?userId=104");
            Assert.Equal(0, history.GetArrayLength());
        }
        var club = await manager.GetFromJsonAsync<JsonElement>("/api/clubs/1");
        Assert.All(club.GetProperty("members").EnumerateArray(), x => Assert.False(x.GetProperty("canWithdraw").GetBoolean()));
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync($"/api/clubs/me/membership-applications/{id}/withdraw", new { userId = 100 })).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ClubDbContext>().MembershipActions.CountAsync());
    }

    [Theory]
    [InlineData("Pending", "UNKNOWN")]
    [InlineData("Pending", "INVITATION")]
    [InlineData("Approved", "APPLICATION")]
    [InlineData("Rejected", "APPLICATION")]
    [InlineData("Inactive", "APPLICATION")]
    public async Task MembershipLifecycle_RejectsWithdrawalOfInvitationsUnknownLegacyAndTerminalStates(string status, string source)
    {
        await using var app = await CreateMembershipLifecycleAppAsync();
        int id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); var row = await db.ClubMemberships.SingleAsync();
            row.Status = status; row.Source = source; id = row.Id; await db.SaveChangesAsync();
        }
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsync($"/api/clubs/me/membership-applications/{id}/withdraw", null)).StatusCode);
    }

    [Fact]
    public async Task MembershipLifecycle_InvitationHasExplicitOriginAndPersonalConsentDoesNotAutoApprove()
    {
        await using var app = await CreateMembershipLifecycleAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); (await db.ClubMemberships.SingleAsync()).IsDeleted = true; await db.SaveChangesAsync();
        }
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var invite = await manager.PostAsJsonAsync("/api/clubs/1/members", new { userId = 104, fullName = "Legacy invite label", role = "CLUB_MEMBER", semesterId = "FALL2026" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        Assert.Equal("Canonical student", (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fullName").GetString());
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var invitations = await student.GetFromJsonAsync<JsonElement>("/api/clubs/me/memberships");
        Assert.Equal("INVITATION", invitations[0].GetProperty("source").GetString());
        Assert.True(invitations[0].GetProperty("canAcceptInvitation").GetBoolean()); Assert.False(invitations[0].GetProperty("canWithdraw").GetBoolean());
        var consent = await student.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload()); Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
        var accepted = await consent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", accepted.GetProperty("status").GetString()); Assert.False(accepted.GetProperty("canAcceptInvitation").GetBoolean());
        Assert.Equal("Canonical student", accepted.GetProperty("fullName").GetString());
    }

    [ClubManagerSqlFact]
    public async Task MembershipLifecycle_SqlServerConcurrentJoinsHaveOneCurrentAttemptAndApprovalCannotRaceWithdrawal()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateMembershipLifecycleAppAsync(database.ConnectionString);
        using var first = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var second = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var joins = await Task.WhenAll(first.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload()), second.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload()));
        Assert.Single(joins, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(joins, x => x.StatusCode == HttpStatusCode.Conflict);
        int id; Guid version;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); var row = await db.ClubMemberships.SingleAsync(); id = row.Id; version = row.ConcurrencyToken;
            Assert.Equal(2, await db.ClubMemberships.IgnoreQueryFilters().CountAsync()); Assert.False(db.Database.HasPendingModelChanges());
        }
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var results = await Task.WhenAll(first.PostAsync($"/api/clubs/me/membership-applications/{id}/withdraw", null),
            manager.PostAsJsonAsync($"/api/clubs/memberships/{id}/approve", new { note = "Reviewed", expectedVersion = version }));
        Assert.Single(results, x => x.IsSuccessStatusCode);
        Assert.All(results, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Conflict }));
        await using var verify = app.Services.CreateAsyncScope(); var context = verify.ServiceProvider.GetRequiredService<ClubDbContext>();
        var final = await context.ClubMemberships.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        Assert.Contains(final.Status, new[] { ClubMembershipStatuses.Approved, ClubMembershipStatuses.Withdrawn });
        Assert.Equal(final.Status == ClubMembershipStatuses.Withdrawn ? 1 : 0, await context.MembershipActions.CountAsync());
    }

    [Theory]
    [InlineData("GET", "/api/clubs/me/membership-applications")]
    [InlineData("POST", "/api/clubs/me/membership-applications/1/withdraw")]
    public Task MembershipLifecycle_GatewayPreservesSelfMembershipRoutes(string method, string path)
        => ClubManager_GatewayRoutesNewApisToOwningServiceBeforeLegacyCatchAll(method, path, "club-service");

    [Theory]
    [InlineData(true, "HAN")]
    [InlineData(false, "HCM")]
    public async Task MembershipLifecycle_InvitationRejectsLockedOrCrossCampusCanonicalAccount(bool locked, string campus)
    {
        await using var app = await CreateMembershipLifecycleAppAsync(targetLocked: locked, targetCampus: campus);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>(); (await db.ClubMemberships.SingleAsync()).IsDeleted = true; await db.SaveChangesAsync();
        }
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Assert.Equal((HttpStatusCode)422, (await manager.PostAsJsonAsync("/api/clubs/1/members",
            new { userId = 104, fullName = "Forged name", role = "CLUB_MEMBER", semesterId = "FALL2026" })).StatusCode);
    }

    [Fact]
    public async Task MembershipLifecycle_AuditRecordsAreAppendOnly()
    {
        await using var app = await CreateMembershipLifecycleAppAsync();
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var created = await student.PostAsJsonAsync("/api/clubs/1/join", MembershipJoinPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await student.PostAsync($"/api/clubs/me/membership-applications/{id}/withdraw", null)).StatusCode);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        (await db.MembershipActions.SingleAsync()).Action = "TAMPER";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); (await db.MembershipActions.SingleAsync()).Action = "SYNC_TAMPER";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear(); db.MembershipActions.Remove(await db.MembershipActions.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(false));
        db.ChangeTracker.Clear(); db.MembershipActions.Remove(await db.MembershipActions.SingleAsync());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges(false));
        Assert.Equal("WITHDRAW", (await db.MembershipActions.AsNoTracking().SingleAsync()).Action);
    }

    [ClubManagerSqlFact]
    public async Task MembershipLifecycle_SqlServerUpgradePreservesLegacyAnswersConsentAndVersionWithoutGuessingOrigin()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var db = new ClubDbContext(new DbContextOptionsBuilder<ClubDbContext>().UseSqlServer(database.ConnectionString).Options);
        await db.Database.MigrateAsync();
        var club = new Club { Code = "LEGACY", Name = "Legacy club" }; db.Clubs.Add(club); await db.SaveChangesAsync();
        var legacy = new ClubMembership
        {
            ClubId = club.Id,
            UserId = 104,
            FullName = "Legacy student",
            Status = ClubMembershipStatuses.Pending,
            AcceptedClubRules = false,
            CommittedToParticipate = false,
            Reason = "Historical answers remain unchanged",
            ReviewNote = "Historical note"
        };
        db.ClubMemberships.Add(legacy); await db.SaveChangesAsync();
        var id = legacy.Id; var version = legacy.ConcurrencyToken;
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.Contains(migrations, x => x.EndsWith("MembershipApplicationHistory", StringComparison.Ordinal));
        Assert.Contains(migrations, x => x.EndsWith("InvitationRosterValidation", StringComparison.Ordinal));
        Assert.EndsWith("InvitationRosterAuditContext", migrations[^1]);
        // Materialize the real preceding schema with legacy data, then exercise its upgrade.
        await db.GetService<IMigrator>().MigrateAsync(migrations.TakeWhile(x => !x.EndsWith("MembershipApplicationHistory", StringComparison.Ordinal)).Last());
        db.ChangeTracker.Clear(); await db.Database.MigrateAsync();
        var upgraded = await db.ClubMemberships.SingleAsync(x => x.Id == id);
        Assert.Equal(version, upgraded.ConcurrencyToken);
        Assert.Equal("Historical answers remain unchanged", upgraded.Reason); Assert.Equal("Historical note", upgraded.ReviewNote);
        Assert.False(upgraded.AcceptedClubRules); Assert.False(upgraded.CommittedToParticipate);
        Assert.Equal(ClubMembershipSources.Unknown, upgraded.Source); Assert.Null(upgraded.ConsentedAtUtc); Assert.Null(upgraded.InvitedAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
