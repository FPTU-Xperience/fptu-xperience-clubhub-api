using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class ActivityActionContractTests
{
    private const string SigningKey = "ActivityActionTests-Only-Long-Signing-Key-2026-Not-Production";
    private static readonly DateTimeOffset Instant = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Application_DraftSubmitRevisionReviewPreservesHistoryAndCreatesOneUnpublishedActivityWithoutPoints()
    {
        await using var f = await Fixture.Create();
        using var manager = f.Client(200);
        var created = await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(), "create");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var value = await Json(created); var id = value.GetProperty("id").GetInt32();
        Assert.Equal(1, value.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("expectedRawXp").ValueKind);
        Assert.Equal("DEFERRED_BY_PRODUCT_OWNER", value.GetProperty("scoringStatus").GetString());
        Assert.Equal(id, (await Json(await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(), "create"))).GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(title: "Different"), "create")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activity-applications/{id}/submit", new { expectedVersion = 1 }, "submit1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Patch, $"/api/activity-applications/{id}", Payload(version: 2), "locked")).StatusCode);
        using var reviewer = f.Client(202);
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, $"/api/activity-applications/{id}/review", new { expectedVersion = 2, decision = "REVISION_REQUESTED", reason = "Clarify scope" }, "review1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activity-applications/{id}", Payload(title: "Revised", version: 3), "edit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activity-applications/{id}/submit", new { expectedVersion = 4 }, "submit2")).StatusCode);
        var decision = new { expectedVersion = 5, decision = "APPROVED", reason = "Operational approval; no points" };
        var approved = await Send(reviewer, HttpMethod.Post, $"/api/activity-applications/{id}/review", decision, "approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var activity = (await Json(approved)).GetProperty("activityId").GetInt32();
        Assert.Equal(activity, (await Json(await Send(reviewer, HttpMethod.Post, $"/api/activity-applications/{id}/review", decision, "approve"))).GetProperty("activityId").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, $"/api/activity-applications/{id}/review", decision, "another-key")).StatusCode);
        var history = await Json(await manager.GetAsync($"/api/activity-applications/{id}/history"));
        Assert.Equal(6, history.GetProperty("items").GetArrayLength());
        Assert.Contains("Workshop", history.GetProperty("items")[0].GetProperty("content").GetString());
        Assert.Equal(2, history.GetProperty("items")[5].GetProperty("revision").GetInt32());
        await f.WithDb(async db =>
        {
            var row = await db.Activities.SingleAsync(); Assert.Equal("DRAFT", row.PublicationStatus);
            Assert.Null(row.PolicyVersion); Assert.Null(row.ScoringSnapshotJson);
            Assert.Equal(1, await db.Set<ActivityExecution>().CountAsync());
            Assert.Equal(1, await db.Set<ActivityActionRule>().CountAsync());
            Assert.Empty(await db.OutboxMessages.Where(x => x.EventType != "recommendation.source.changed").ToArrayAsync());
        });
    }

    [Theory]
    [InlineData(100, HttpStatusCode.Forbidden)]
    [InlineData(201, HttpStatusCode.Forbidden)]
    [InlineData(203, HttpStatusCode.Forbidden)]
    public async Task Application_CreationRequiresCurrentManagerAndCampus(int actor, HttpStatusCode expected)
    {
        await using var f = await Fixture.Create(); using var client = f.Client(actor);
        Assert.Equal(expected, (await Send(client, HttpMethod.Post, "/api/activity-applications", Payload(), "x")).StatusCode);
        await f.WithDb(async db => Assert.Empty(await db.Set<ActivityApplication>().ToArrayAsync()));
    }

    [Theory]
    [InlineData("{\"clubId\":1,\"clubId\":2}")]
    [InlineData("{\"clubId\":1,\"expectedRawXp\":999}")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Application_RejectsDuplicateUnknownAndMalformedJson(string body)
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/activity-applications")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", "x");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Application_RejectsUnboundedCountsMissingKeyInvalidSemesterAndMissingAuthority()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/activity-applications", Payload())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(capacity: 1000001), "large")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(period: "unknown"), "period")).StatusCode);
        using var huge = new HttpRequestMessage(HttpMethod.Post, "/api/activity-applications")
        { Content = new StringContent("{\"title\":\"" + new string('x', 270000) + "\"}", Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await manager.SendAsync(huge)).StatusCode);
        f.AuthorityAvailable = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(), "outage")).StatusCode);
    }

    [Fact]
    public async Task Application_CannotReadOtherClubReviewOwnApplicationOrReplayAfterRevocation()
    {
        await using var f = await Fixture.Create(); var (id, _) = await f.Approved();
        using var outsider = f.Client(201); using var student = f.Client(100);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/activity-applications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/activity-applications/{id}/history")).StatusCode);
        using var wrongCampus = f.Client(203);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongCampus.GetAsync($"/api/activity-applications/{id}")).StatusCode);
        using var selfReviewer = f.Client(200, AuthRoles.StudentAffairsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(selfReviewer, HttpMethod.Post, $"/api/activity-applications/{id}/review", new { expectedVersion = 3, decision = "APPROVED" }, "self")).StatusCode);
        f.ManagerEnabled = false; using var manager = f.Client(200);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(manager, HttpMethod.Post, $"/api/activity-applications/{id}/submit", new { expectedVersion = 1 }, "submit")).StatusCode);
    }

    [Fact]
    public async Task Staffing_RecruitmentAndWorkspaceUseSameIdsJoinIsSelfOnlyCapacitySafeAndVersioned()
    {
        await using var f = await Fixture.Create(); var (_, activity) = await f.Approved(); using var manager = f.Client(200);
        var staffing = await Json(await manager.GetAsync($"/api/activities/{activity}/staffing"));
        var rule = staffing.GetProperty("actions")[0].GetProperty("id").GetInt32();
        Assert.True(rule > 0);
        using var student = f.Client(100);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/activities/{activity}/staffing")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = true, expectedVersion = 1 }, "publish")).StatusCode);
        var recruitment = await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/recruitment", new
        {
            title = "Staff",
            description = "Support",
            visibility = "PUBLIC",
            eligibility = "SCHOOL_STUDENTS",
            expectedVersion = 2,
            positions = new[] { new { actionRuleId = rule, capacity = 1, assignmentKind = "STAFF" } }
        }, "recruit");
        Assert.Equal(HttpStatusCode.OK, recruitment.StatusCode);
        var rid = (await Json(recruitment)).GetProperty("id").GetGuid();
        var list = await Json(await student.GetAsync("/api/clubs/1/activity-recruitments"));
        Assert.Equal(rid, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/recruitment/status", new { expectedVersion = 3, status = "OPEN" }, "open")).StatusCode);
        staffing = await Json(await student.GetAsync($"/api/activities/{activity}/staffing"));
        Assert.Empty(staffing.GetProperty("assignments").EnumerateArray());
        Assert.Empty(staffing.GetProperty("eligibleAssignees").EnumerateArray());
        var position = staffing.GetProperty("recruitment").GetProperty("positions")[0].GetProperty("id").GetGuid();
        var join = $"/api/activities/{activity}/recruitment/positions/{position}/join";
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(student, HttpMethod.Post, join, new { expectedVersion = 4, userId = 101 }, "forged")).StatusCode);
        var joined = await Send(student, HttpMethod.Post, join, new { expectedVersion = 4 }, "join"); Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var assignment = (await Json(joined)).GetProperty("id").GetGuid();
        Assert.Equal(assignment, (await Json(await Send(student, HttpMethod.Post, join, new { expectedVersion = 4 }, "join"))).GetProperty("id").GetGuid());
        using var second = f.Client(101);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(second, HttpMethod.Post, join, new { expectedVersion = 5 }, "seat")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = false, expectedVersion = 2 }, "hide-used")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/assignments/{assignment}/cancel", new { expectedVersion = 5 }, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(second, HttpMethod.Post, join, new { expectedVersion = 6 }, "seat-after-cancel")).StatusCode);
        await f.WithDb(async db =>
        {
            Assert.Equal(2, await db.Set<ActivityStaffAssignment>().CountAsync());
            Assert.Equal(1, await db.Set<ActivityStaffAssignment>().CountAsync(x => x.Status != "REMOVED"));
            Assert.Empty(await db.OutboxMessages.Where(x => x.EventType != "recommendation.source.changed").ToArrayAsync());
        });
    }

    [Fact]
    public async Task Staffing_PrivateRecruitmentStaysPrivateAndLegacyLifecycleCannotBypassApprovedDossier()
    {
        await using var f = await Fixture.Create(); var (_, activity) = await f.Approved(); using var manager = f.Client(200);
        var staffing = await Json(await manager.GetAsync($"/api/activities/{activity}/staffing")); var rule = staffing.GetProperty("actions")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = true, expectedVersion = 1 }, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/recruitment", new
        {
            title = "Private",
            visibility = "PRIVATE",
            eligibility = "CLUB_MEMBERS",
            expectedVersion = 2,
            positions = new[] { new { actionRuleId = rule, capacity = 1, assignmentKind = "HOST" } }
        }, "recruit")).StatusCode);
        using var nonmember = f.Client(101);
        Assert.Empty((await Json(await nonmember.GetAsync("/api/clubs/1/activity-recruitments"))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await nonmember.GetAsync($"/api/activities/{activity}/staffing")).StatusCode);
        await f.WithDb(async db =>
        {
            var activityRow = await db.Activities.SingleAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Put, $"/api/activities/{activity}", new
            { operation = "UPDATE", expectedVersion = activityRow.Version, title = "Bypass" }, "legacy")).StatusCode);
        });
    }

    [Fact]
    public async Task Staffing_CompletionVerificationRequiresIndependentCurrentCapabilityAndNeverAwardsPoints()
    {
        await using var f = await Fixture.Create(); var (_, activity) = await f.Approved(); using var manager = f.Client(200);
        var staffing = await Json(await manager.GetAsync($"/api/activities/{activity}/staffing")); var rule = staffing.GetProperty("actions")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = true, expectedVersion = 1 }, "publish")).StatusCode);
        var assigned = await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/assignments", new { expectedVersion = 2, userId = 100, actionRuleId = rule, assignmentKind = "HOST" }, "assign");
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode); var assignment = (await Json(assigned)).GetProperty("id").GetGuid();
        f.Clock.Utc = Instant.AddDays(2);
        using var student = f.Client(100);
        var completed = await Send(student, HttpMethod.Post, $"/api/activities/{activity}/assignments/{assignment}/completions", new
        { expectedVersion = 3, occurrenceKey = "output-1", occurrenceLabel = "First output", evidence = "Delivered and documented" }, "completion");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode); var completion = (await Json(completed)).GetProperty("id").GetGuid();
        var verify = $"/api/activities/{activity}/action-completions/{completion}/verify";
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(student, HttpMethod.Post, verify, new { expectedVersion = 4, completionVersion = 1 }, "self")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, HttpMethod.Post, verify, new { expectedVersion = 4, completionVersion = 1 }, "manager")).StatusCode);
        using var verifier = f.Client(300);
        var verified = await Send(verifier, HttpMethod.Post, verify, new { expectedVersion = 4, completionVersion = 1 }, "verify");
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var receipt = await Json(verified); Assert.False(receipt.GetProperty("canAwardPoints").GetBoolean());
        Assert.Equal(JsonValueKind.Null, receipt.GetProperty("awardedRawXp").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await Send(verifier, HttpMethod.Post, verify, new { expectedVersion = 4, completionVersion = 1 }, "verify")).StatusCode);
        f.VerifierEnabled = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(verifier, HttpMethod.Post, verify, new { expectedVersion = 4, completionVersion = 1 }, "verify")).StatusCode);
        await f.WithDb(async db =>
        {
            var row = await db.Set<ActivityActionCompletion>().SingleAsync(); Assert.Equal("VERIFIED", row.Status); Assert.Equal(300, row.VerifiedBy);
            Assert.Single(await db.Set<ActivityExecutionAudit>().Where(x => x.Action == "COMPLETION_VERIFIED_WITHOUT_POINTS").ToArrayAsync());
            Assert.Empty(await db.OutboxMessages.Where(x => x.EventType != "recommendation.source.changed").ToArrayAsync());
        });
    }

    [Fact]
    public async Task Publication_GetReturnsIntegerPublicationMetadataWhileKeepingLegacyGuidVersion()
    {
        await using var f = await Fixture.Create(sql: true); var (application, activity) = await f.Approved(); using var manager = f.Client(200);
        var raw = await Json(await manager.GetAsync($"/api/activities/{activity}"));
        Assert.Equal(1, raw.GetProperty("publicationVersion").GetInt32()); Assert.False(raw.GetProperty("isPublished").GetBoolean());
        Assert.Equal(application, raw.GetProperty("sourceApplicationId").GetInt32()); Assert.True(raw.GetProperty("publicationCapabilities").GetProperty("canManage").GetBoolean());
        Assert.NotEqual(Guid.Empty, raw.GetProperty("version").GetGuid());
        Assert.False(raw.TryGetProperty("publicationStatus", out _)); // FE chooses the action bool API, not legacy PUBLISH/CLOSE.
        var list = await Json(await manager.GetAsync("/api/activities?clubId=1"));
        Assert.Equal(1, list[0].GetProperty("publicationVersion").GetInt32());
        f.SemesterActive = false;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = true, expectedVersion = 1 }, "inactive")).StatusCode);
        raw = await Json(await manager.GetAsync($"/api/activities/{activity}"));
        Assert.False(raw.GetProperty("publicationCapabilities").GetProperty("canManage").GetBoolean());
    }

    [Fact]
    public async Task Attendance_ManualClaimRequiresAttendanceAuthorityAndDoesNotCreateScoredAttendanceOrAwards()
    {
        await using var f = await Fixture.Create(); var (_, activity) = await f.Approved("ATTENDANCE"); using var manager = f.Client(200);
        var data = await Json(await manager.GetAsync($"/api/activities/{activity}/staffing")); var rule = data.GetProperty("actions")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = true, expectedVersion = 1 }, "publish")).StatusCode);
        var assigned = await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/assignments", new { userId = 100, actionRuleId = rule, assignmentKind = "STAFF", expectedVersion = 2 }, "assign");
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode); var assignment = (await Json(assigned)).GetProperty("id").GetGuid();
        f.Clock.Utc = Instant.AddDays(2); using var student = f.Client(100);
        var claim = new { expectedVersion = 3, occurrenceKey = "meeting-1", occurrenceLabel = "Attendance", evidence = "Please independently confirm my attendance" };
        Assert.Equal(HttpStatusCode.Conflict, (await Send(student, HttpMethod.Post, $"/api/activities/{activity}/assignments/{assignment}/completions", claim, "wrong-source")).StatusCode);
        var response = await Send(student, HttpMethod.Post, $"/api/activities/{activity}/assignments/{assignment}/attendance-claims", claim, "claim");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var completion = (await Json(response)).GetProperty("id").GetGuid();
        using var verifier = f.Client(300); f.AttendanceVerification = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(verifier, HttpMethod.Post, $"/api/activities/{activity}/action-completions/{completion}/verify", new { expectedVersion = 4, completionVersion = 1 }, "work-only")).StatusCode);
        f.AttendanceVerification = true;
        Assert.Equal(HttpStatusCode.OK, (await Send(verifier, HttpMethod.Post, $"/api/activities/{activity}/action-completions/{completion}/verify", new { expectedVersion = 4, completionVersion = 1 }, "attendance-verifier")).StatusCode);
        await f.WithDb(async db =>
        {
            Assert.Equal("VERIFIED", (await db.Set<ActivityActionCompletion>().SingleAsync()).Status);
            Assert.Empty(await db.ActivityAttendances.ToArrayAsync()); Assert.Empty(await db.OutboxMessages.Where(x => x.EventType != "recommendation.source.changed").ToArrayAsync());
        });
    }

    private static object Payload(string title = "Workshop", int? version = null, string period = "FA2026", int capacity = 2, string method = "WORK_REVIEW") => new
    {
        clubId = 1,
        period,
        title,
        description = "Workshop description",
        objective = "Learn together",
        location = "Hall",
        startTimeUtc = Instant.AddDays(1),
        endTimeUtc = Instant.AddDays(3),
        scale = "Club",
        independentActionsConfirmed = true,
        actions = new[] { new { clientId = "work", name = "Produce output", basePoints = 50m, contributionRole = "Contributor",
            scaleSource = "ACTIVITY", scale = (string?)null, expectedPeople = capacity, expectedOccurrencesPerPerson = 1,
            maxAwardsPerPerson = 1, maxAwardCount = capacity, verificationMethod = method, completionMode = "FULL_ONLY", stackingRule = "ADDITIVE" } },
        recruitment = (object?)null,
        expectedVersion = version
    };
    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string key)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request);
    }
    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Application_ApprovalReasonIsOptionalAndPublicationFlagIsRequired()
    {
        await using var f = await Fixture.Create(); using var manager = f.Client(200); using var reviewer = f.Client(202);
        var id = (await Json(await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(), "new"))).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activity-applications/{id}/submit", new { expectedVersion = 1 }, "submit")).StatusCode);
        var approved = await Send(reviewer, HttpMethod.Post, $"/api/activity-applications/{id}/review", new { expectedVersion = 2, decision = "APPROVED" }, "approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var activity = (await Json(approved)).GetProperty("activityId").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { expectedVersion = 1 }, "missing-flag")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Application_HistoryAndExecutionAuditAreAppendOnlyForEverySaveOverload(int mode)
    {
        await using var f = await Fixture.Create(); await f.Approved();
        await f.WithDb(async db =>
        {
            var history = await db.Set<ActivityApplicationHistory>().FirstAsync(); history.ContentJson = "{}";
            async Task Save()
            {
                if (mode == 0) db.SaveChanges();
                else if (mode == 1) db.SaveChanges(false);
                else if (mode == 2) await db.SaveChangesAsync();
                else await db.SaveChangesAsync(false);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(Save);
            db.ChangeTracker.Clear(); var audit = await db.Set<ActivityExecutionAudit>().FirstAsync(); db.Remove(audit);
            await Assert.ThrowsAsync<InvalidOperationException>(Save);
        });
    }

    [Fact]
    public async Task Staffing_SqlServerUpgradePreservesLegacyRowsAndConcurrentLastSeatCreatesOneAssignment()
    {
        await using var f = await Fixture.Create(sql: true, upgrade: true); var (_, activity) = await f.Approved();
        using var manager = f.Client(200); using var one = f.Client(100); using var two = f.Client(101);
        var staffing = await Json(await manager.GetAsync($"/api/activities/{activity}/staffing"));
        var rule = staffing.GetProperty("actions")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = true, expectedVersion = 1 }, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/recruitment", new
        {
            expectedVersion = 2,
            title = "Last seat",
            visibility = "PUBLIC",
            eligibility = "SCHOOL_STUDENTS",
            positions = new[] { new { actionRuleId = rule, capacity = 1, assignmentKind = "STAFF" } }
        }, "recruit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/recruitment/status", new { expectedVersion = 3, status = "OPEN" }, "open")).StatusCode);
        staffing = await Json(await one.GetAsync($"/api/activities/{activity}/staffing"));
        var position = staffing.GetProperty("recruitment").GetProperty("positions")[0].GetProperty("id").GetGuid();
        var url = $"/api/activities/{activity}/recruitment/positions/{position}/join";
        var results = await Task.WhenAll(Send(one, HttpMethod.Post, url, new { expectedVersion = 4 }, "one"),
            Send(two, HttpMethod.Post, url, new { expectedVersion = 4 }, "two"));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var list = await one.GetAsync("/api/clubs/1/activity-recruitments?from=2026-10-22T00:00:00Z&to=2026-10-30T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode); Assert.Single((await Json(list)).GetProperty("items").EnumerateArray());
        await f.WithDb(async db =>
        {
            Assert.Equal(2, await db.Activities.CountAsync()); Assert.Equal("Legacy row", (await db.Activities.SingleAsync(x => x.Title == "Legacy row")).Title);
            Assert.Equal(1, await db.Set<ActivityStaffAssignment>().CountAsync(x => x.ActivityId == activity));
            Assert.Single(await db.Set<ActivityExecutionAudit>().Where(x => x.Action == "JOIN_POSITION").ToArrayAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    private sealed class Fixture(SqliteConnection? connection, WebApplication app, string? sqlConnection) : IAsyncDisposable
    {
        internal bool ManagerEnabled { get; set; } = true;
        internal bool VerifierEnabled { get; set; } = true;
        internal bool AuthorityAvailable { get; set; } = true;
        internal bool SemesterActive { get; set; } = true;
        internal bool AttendanceVerification { get; set; } = true;
        internal TestClock Clock { get; } = new();
        internal static async Task<Fixture> Create(bool sql = false, bool upgrade = false)
        {
            var connection = sql ? null : new SqliteConnection("Data Source=:memory:");
            if (connection is not null) await connection.OpenAsync();
            var sqlConnection = sql ? new SqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")
                ?? "Server=(localdb)\\ClubManagerApiTests;Integrated Security=True;TrustServerCertificate=True")
            {
                InitialCatalog = "ClubHub_ActionTests_" + Guid.NewGuid().ToString("N"),
            }.ConnectionString : null;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = SigningKey,
                ["Security:InternalWorkflowToken"] = "test-internal-token"
            });
            Fixture f = null!;
            var handler = new Handler(async r =>
            {
                if (!f.AuthorityAvailable) return new(HttpStatusCode.ServiceUnavailable);
                var path = r.RequestUri!.AbsolutePath;
                var actor = r.Headers.Authorization?.Parameter is { } token ? int.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject) : 0;
                object? result = null;
                if (path.EndsWith("/user-profiles", StringComparison.Ordinal))
                {
                    var ids = (await r.Content!.ReadFromJsonAsync<int[]>())!;
                    result = ids.Select(id => new InternalUserProfile(id, "User " + id, id + "@example.test", id == 203 ? "HCM" : "HN", true, false, null)).ToArray();
                }
                else if (path.StartsWith("/api/internal/experience/clubs/", StringComparison.Ordinal))
                {
                    var subject = int.Parse(r.RequestUri.Query.Split('=').Last());
                    result = new ClubExperienceAuthority(1, "Club One", "HN", true, subject is 100 or 200, Instant.AddDays(-10));
                }
                else if (path == "/api/clubs/me/access")
                    result = actor == 200 && f.ManagerEnabled ? new[] { new ClubAccessSnapshot(1, "Club One", true, false, true, [200], [100, 200]) } : Array.Empty<ClubAccessSnapshot>();
                else if (path.StartsWith("/api/internal/experience/semesters/FA2026", StringComparison.Ordinal))
                    result = new SemesterReference(Guid.Parse("11111111-1111-1111-1111-111111111111"), "FA2026", Instant.AddDays(-30), Instant.AddDays(60), f.SemesterActive);
                else if (path == "/api/internal/experience/capabilities")
                    result = new VerificationCapabilityResponse(actor == 300 && f.VerifierEnabled && (!r.RequestUri.Query.Contains("attendance.verify", StringComparison.Ordinal) || f.AttendanceVerification));
                else if (path.EndsWith("/member-roster/resolve", StringComparison.Ordinal))
                    result = new[] { new ClubMemberRosterItem(1, 100, "User 100", "100@example.test", "", "Member", "Approved", Instant.AddDays(-10)) };
                else if (path.EndsWith("/member-roster", StringComparison.Ordinal))
                    result = new ClubMemberRosterPage([new ClubMemberRosterItem(1, 100, "User 100", "100@example.test", "", "Member", "Approved", Instant.AddDays(-10))], 1, 100, 1, 1);
                return result is null ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
            });
            HttpClient Http() => new(handler, false) { BaseAddress = new Uri("http://authority/") };
            builder.Services.AddDbContext<ActivityDbContext>(x => { if (sqlConnection is null) x.UseSqlite(connection!); else x.UseSqlServer(sqlConnection); });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddSingleton(new ClubAccessClient(Http(), new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<ClubAccessClient>.Instance));
            builder.Services.AddSingleton(new CurrentUserProfileClient(Http(), builder.Configuration));
            builder.Services.AddSingleton(new ClubExperienceAuthorityClient(Http(), builder.Configuration));
            builder.Services.AddSingleton(new SemesterCatalogClient(Http(), builder.Configuration));
            builder.Services.AddSingleton(new ExperienceAuthorityClient(Http(), builder.Configuration));
            builder.Services.AddSingleton(new ClubMemberRosterClient(Http()));
            builder.Services.AddSingleton(new ReportVerificationClient(Http(), NullLogger<ReportVerificationClient>.Instance));
            builder.Services.AddSingleton<TimeProvider>(_ => f.Clock);
            var app = builder.Build(); f = new(connection, app, sqlConnection);
            app.UseAuthentication(); app.UseAuthorization(); app.MapActivityEndpoints(); app.MapActivityApplicationEndpoints(); app.MapActivityStaffingEndpoints();
            try
            {
                await f.WithDb(async db =>
                {
                    if (!sql) { await db.Database.EnsureCreatedAsync(); return; }
                    if (upgrade)
                    {
                        await db.GetService<IMigrator>().MigrateAsync("20261013142006_StudentActivityDiscovery");
                        db.Add(new ClubActivity
                        {
                            ClubId = 1,
                            ClubName = "Legacy",
                            Title = "Legacy row",
                            Description = "Preserved",
                            Location = "Hall",
                            StartTimeUtc = Instant.AddDays(-2),
                            EndTimeUtc = Instant.AddDays(-1),
                            CreatedByUserId = 200
                        });
                        await db.SaveChangesAsync();
                    }
                    await db.Database.MigrateAsync();
                });
            }
            catch { await f.DisposeAsync(); throw; }
            await app.StartAsync(); return f;
        }
        internal HttpClient Client(int actor, string? role = null)
        {
            role ??= actor is 200 or 201 ? AuthRoles.ClubManager : actor is 202 or 203 ? AuthRoles.StudentAffairsAdmin : AuthRoles.ClubMember;
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client",
                [new Claim(JwtRegisteredClaimNames.Sub, actor.ToString()), new Claim(ClaimTypes.Role, role)],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task<(int Id, int ActivityId)> Approved(string method = "WORK_REVIEW")
        {
            using var manager = Client(200); using var reviewer = Client(202);
            var created = await Send(manager, HttpMethod.Post, "/api/activity-applications", Payload(method: method), "create");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode); var id = (await Json(created)).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activity-applications/{id}/submit", new { expectedVersion = 1 }, "submit")).StatusCode);
            var approved = await Send(reviewer, HttpMethod.Post, $"/api/activity-applications/{id}/review", new { expectedVersion = 2, decision = "APPROVED", reason = "Approved without points" }, "approve");
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode); return (id, (await Json(approved)).GetProperty("activityId").GetInt32());
        }
        internal async Task WithDb(Func<ActivityDbContext, Task> action)
        { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<ActivityDbContext>()); }
        public async ValueTask DisposeAsync()
        {
            if (sqlConnection is not null)
            {
                var identity = new SqlConnectionStringBuilder(sqlConnection);
                if (!identity.InitialCatalog.StartsWith("ClubHub_ActionTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test database.");
                await WithDb(async db => await db.Database.EnsureDeletedAsync());
            }
            await app.DisposeAsync(); if (connection is not null) await connection.DisposeAsync();
        }
    }
    private sealed class TestClock : TimeProvider
    { internal DateTimeOffset Utc { get; set; } = Instant; public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
}
