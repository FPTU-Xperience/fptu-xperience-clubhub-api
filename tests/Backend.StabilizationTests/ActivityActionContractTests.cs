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
using ClubReportHub.Shared.Data;
using ClubReportHub.Shared.Events;
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
    private const string TransparentPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACklEQVR4nGNgAAAAAgABSK+kcQAAAABJRU5ErkJggg==";
    private static readonly Guid CampusPolicyVersion = Guid.Parse("04040404-0404-0404-0404-040404040404");
    private static readonly DateTimeOffset Instant = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClosingReportReleasesAttendanceXpOnlyForConfirmedOutsideParticipants()
    {
        await using var f = await Fixture.Create();
        var (_, id) = await f.Approved();
        using var manager = f.Client(200);
        using var reviewer = f.Client(202);
        var ids = new Dictionary<int, Guid>();
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed;
            activity.UpdatedAtUtc = Instant;
            var session = new ActivitySession { ActivityId = id, SemesterId = "FA2026", SeriesId = Guid.NewGuid() };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
            foreach (var userId in new[] { 100, 101, 102, 103 })
            {
                var attendance = new ActivityAttendance
                {
                    ActivityId = id, SessionId = session.Id, UserId = userId, FullName = $"Student {userId}",
                    AttendanceDate = DateOnly.FromDateTime(Instant.UtcDateTime), VerificationStatus = "VERIFIED"
                };
                db.ActivityAttendances.Add(attendance);
                if (userId != 103) db.Add(new ActivityFinalRole
                {
                    ActivityId = id, UserId = userId, Role = userId == 102 ? "HOST" : "PARTICIPANT",
                    Version = Guid.NewGuid(), ConfirmedAtUtc = Instant, DisplayName = attendance.FullName
                });
                await db.SaveChangesAsync();
                var source = new ExperienceSourceSnapshot(id, 1, session.Id, "FA2026", "Workshop",
                    Instant.AddHours(-2), Instant.AddHours(-1), activity.Version, session.Version, "CLOSED", false,
                    new ContributionAttributes("T1", "Participant", "Club", "None", []),
                    new ExperiencePolicyReference(Guid.NewGuid(), "V1", "FA2026", null, null, "APPROVED", "PROJECT", 2, "REVERSE"));
                var eventId = Guid.NewGuid();
                ids.Add(userId, eventId);
                db.AddOutboxMessage(new AttendanceVerifiedEvent(eventId, Instant.AddHours(-1), userId, attendance.Id, "ROTATING_QR", null, source),
                    ExperienceRoutingKeys.AttendanceVerified);
            }
            await db.SaveChangesAsync();
            Assert.Equal(4, await db.OutboxMessages.CountAsync(x => x.Status == "AwaitingClosingReport"));
        });
        var path = $"/api/activities/{id}/closing-report";
        var submitted = await Send(manager, HttpMethod.Post, path, new { summary = "Completed" }, "xp-report");
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var version = (await Json(submitted)).GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, $"{path}/approve",
            new { expectedVersion = version }, "approve-xp-report")).StatusCode);
        await f.WithDb(async db =>
        {
            var rows = await db.OutboxMessages.Where(x => x.EventType == ExperienceRoutingKeys.AttendanceVerified).ToDictionaryAsync(x => x.Id);
            Assert.Equal(OutboxMessageStatus.Pending, rows[ids[100]].Status);
            Assert.Equal("PARTICIPANT", JsonDocument.Parse(rows[ids[100]].Payload).RootElement.GetProperty("closingReportReviewRole").GetString());
            Assert.Equal(OutboxMessageStatus.Pending, rows[ids[101]].Status);
            Assert.Equal(OutboxMessageStatus.Pending, rows[ids[102]].Status);
            Assert.Equal("HOST", JsonDocument.Parse(rows[ids[102]].Payload).RootElement.GetProperty("closingReportReviewRole").GetString());
            Assert.Equal("ClosingReportIneligible", rows[ids[103]].Status);
        });
    }

    [Theory]
    [InlineData("ACCEPTED", 1)]
    [InlineData("REJECTED", 0)]
    public async Task CampusHandoverTransfersOwnershipOnlyOnAcceptance(string decision, int ownerClub)
    {
        await using var f = await Fixture.Create();
        using var reviewer = f.Client(202);
        using var manager = f.Client(200);
        using var outsider = f.Client(203);
        const string campus = "/api/activities/campus";
        var draft = await CreateCampusDraft(reviewer);
        var id = draft.GetProperty("id").GetInt32();
        var version = draft.GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(outsider, HttpMethod.Post, $"{campus}/{id}/handover",
            new { expectedVersion = version, clubId = 1 }, "cross-campus-offer")).StatusCode);
        var offered = await Send(reviewer, HttpMethod.Post, $"{campus}/{id}/handover",
            new { expectedVersion = version, clubId = 1 }, "offer-campus-activity");
        Assert.Equal(HttpStatusCode.OK, offered.StatusCode);
        var pending = await Json(offered);
        Assert.Equal(0, pending.GetProperty("clubId").GetInt32());
        var decided = await Send(manager, HttpMethod.Post, $"/api/activities/handover/club/1/{id}/decision",
            new { expectedVersion = pending.GetProperty("handover").GetProperty("version").GetGuid(), decision }, "decide-campus-offer");
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal(ownerClub, (await Json(decided)).GetProperty("clubId").GetInt32());
    }

    [Fact]
    public async Task CampusFinalizationClosesRecapAndIssuesAllCertificatesForTheConfirmedRole()
    {
        await using var f = await Fixture.Create();
        using var reviewer = f.Client(202);
        using var student = f.Client(100);
        using var otherStudent = f.Client(101);
        var draft = await CreateCampusDraft(reviewer);
        var id = draft.GetProperty("id").GetInt32();
        var path = $"/api/activities/campus/{id}";
        var start = Instant.AddDays(3);
        var configured = await Send(reviewer, HttpMethod.Put, $"{path}/xp-policy", new
        {
            expectedVersion = draft.GetProperty("version").GetGuid(), policyVersion = CampusPolicyVersion,
            tier = "T2", pillarWeights = new[] { new { pillar = "Academic", weight = 1 } }
        }, "configure-campus-xp");
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        var assignment = await Send(reviewer, HttpMethod.Post, $"{path}/duties",
            new { userId = 101, role = "HOST", responsibility = "Host the workshop" }, "campus-host");
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        var duty = await Json(assignment);
        var session = await Send(reviewer, HttpMethod.Post, $"{path}/sessions", new
        {
            expectedVersion = (await Json(configured)).GetProperty("version").GetGuid(), location = "Hall",
            startTimeUtc = start, endTimeUtc = start.AddHours(1), opensAt = start, closesAt = start.AddHours(1)
        }, "campus-session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var savedSession = await Json(session);
        var sessionId = savedSession.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, $"{path}/publish", new
        {
            expectedVersion = savedSession.GetProperty("activityVersion").GetGuid(),
            registrationOpensAt = Instant.AddHours(1), registrationClosesAt = start
        }, "publish-campus-activity")).StatusCode);
        f.Clock.Utc = Instant.AddHours(2);
        Assert.Equal(HttpStatusCode.OK, (await Send(student, HttpMethod.Post, $"/api/activities/{id}/participants",
            new { }, "register-campus")).StatusCode);
        var attendancePath = $"{path}/sessions/{sessionId}/attendance";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, attendancePath,
            new { userId = 100 }, "early-campus-attendance")).StatusCode);
        f.Clock.Utc = start;
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, attendancePath,
            new { userId = 100 }, "verify-campus-attendance")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post,
            $"{path}/duties/{duty.GetProperty("id").GetGuid()}/confirm",
            new { expectedVersion = duty.GetProperty("version").GetGuid() }, "confirm-campus-host")).StatusCode);
        await f.WithDb(async db => Assert.Single(await db.OutboxMessages.Where(x => x.Status == "AwaitingClosingReport").ToListAsync()));
        var certificates = $"/api/activities/{id}/certificate-definitions";
        for (var i = 1; i <= 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, certificates,
                new { role = "PARTICIPANT", title = $"Certificate {i}", badgePng = TransparentPng, framePng = TransparentPng }, $"define-{i}")).StatusCode);
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed;
            activity.UpdatedAtUtc = start.AddHours(2);
            await db.SaveChangesAsync();
        });
        f.Clock.Utc = start.AddHours(2);
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Put, $"/api/activities/{id}/final-roles/100",
            new { role = "PARTICIPANT" }, "campus-final-role")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Put, $"/api/activities/{id}/final-roles/101",
            new { role = "HOST" }, "campus-host-final-role")).StatusCode);
        var recap = $"/api/activities/{id}/recap";
        Assert.True((await student.GetFromJsonAsync<JsonElement>(recap)).GetProperty("canSubmit").GetBoolean());
        var reportPath = $"/api/activities/campus/{id}/report";
        var created = await Send(reviewer, HttpMethod.Post, reportPath, new { summary = "Campus activity completed" }, "campus-report");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var report = await Json(created);
        Assert.Empty((await student.GetFromJsonAsync<JsonElement>("/api/activities/certificates/me")).GetProperty("items").EnumerateArray());
        var version = report.GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, $"{reportPath}/finalize", new { expectedVersion = version }, "finalize-campus-report")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, $"{reportPath}/finalize", new { expectedVersion = version }, "finalize-again")).StatusCode);
        Guid activityVersion = default, sessionVersion = default;
        await f.WithDb(async db =>
        {
            activityVersion = (await db.Activities.SingleAsync()).Version;
            sessionVersion = (await db.Sessions.SingleAsync()).Version;
        });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Delete, $"/api/activities/{id}",
            new { expectedVersion = activityVersion, reason = "Try to cancel finalized activity" }, "cancel-finalized-activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, $"/api/activities/{id}/sessions/{sessionId}/cancel",
            new { expectedVersion = sessionVersion, reason = "Try to cancel finalized session" }, "cancel-finalized-session")).StatusCode);
        Assert.False((await student.GetFromJsonAsync<JsonElement>(recap)).GetProperty("canSubmit").GetBoolean());
        await f.WithDb(async db =>
        {
            var attendanceXp = await db.OutboxMessages.SingleAsync(x => x.EventType == ExperienceRoutingKeys.AttendanceVerified);
            Assert.Equal(OutboxMessageStatus.Pending, attendanceXp.Status);
            var dutyXp = await db.OutboxMessages.SingleAsync(x => x.EventType == ExperienceRoutingKeys.ClosingDutyXp);
            Assert.Equal(101, JsonDocument.Parse(dutyXp.Payload).RootElement.GetProperty("userId").GetInt32());
        });
        var issued = (await student.GetFromJsonAsync<JsonElement>("/api/activities/certificates/me")).GetProperty("items");
        Assert.Equal(2, issued.GetArrayLength());
        var certificateId = issued[0].GetProperty("id").GetGuid();
        const string displayPath = "/api/activities/certificates/me/display";
        var selection = new { expectedVersion = Guid.Empty, badgeId = certificateId, frameId = certificateId, badgeX = 50, badgeY = 50, badgeSize = 15 };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(otherStudent, HttpMethod.Put, displayPath, selection, "equip-unowned")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(student, HttpMethod.Put, displayPath, selection, "equip-owned")).StatusCode);
        Assert.Equal(TransparentPng, (await student.GetFromJsonAsync<JsonElement>(displayPath)).GetProperty("framePng").GetString());
        Assert.Empty((await otherStudent.GetFromJsonAsync<JsonElement>("/api/activities/certificates/me")).GetProperty("items").EnumerateArray());
    }

    private static async Task<JsonElement> CreateCampusDraft(HttpClient reviewer)
    {
        var created = await Send(reviewer, HttpMethod.Post, "/api/activities/campus", new
        {
            title = "Campus workshop", description = "Workshop for students", location = "Hall",
            semesterId = "FA2026", startTimeUtc = Instant.AddDays(3), endTimeUtc = Instant.AddDays(3).AddHours(2)
        }, "campus-draft");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return await Json(created);
    }

    [Fact]
    public async Task CampusDraftStaysPrivateWhenPublicationIsScheduledAndCanBeCancelled()
    {
        await using var f = await Fixture.Create();
        using var reviewer = f.Client(202);
        var draft = await CreateCampusDraft(reviewer);
        var id = draft.GetProperty("id").GetInt32();
        var path = $"/api/activities/campus/{id}";
        var publishAt = Instant.AddHours(1);
        var scheduled = await Send(reviewer, HttpMethod.Post, $"{path}/publish", new
        {
            expectedVersion = draft.GetProperty("version").GetGuid(), publishAtUtc = publishAt,
            registrationOpensAt = publishAt.AddHours(1), registrationClosesAt = Instant.AddDays(3)
        }, "schedule-campus");
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var view = await Json(scheduled);
        Assert.Equal("DRAFT", view.GetProperty("publicationStatus").GetString());
        Assert.Equal(publishAt, view.GetProperty("publishAtUtc").GetDateTimeOffset());
        var cancelled = await Send(reviewer, HttpMethod.Post, $"{path}/cancel-publication",
            new { expectedVersion = view.GetProperty("version").GetGuid() }, "cancel-campus");
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(cancelled)).GetProperty("publishAtUtc").ValueKind);
    }

    [Fact]
    public async Task CampusDutyMakesHostEligibleOnlyAfterIndependentConfirmation()
    {
        await using var f = await Fixture.Create();
        using var reviewer = f.Client(202);
        using var outsider = f.Client(203);
        var draft = await CreateCampusDraft(reviewer);
        var id = draft.GetProperty("id").GetInt32();
        var path = $"/api/activities/campus/{id}";
        var assigned = await Send(reviewer, HttpMethod.Post, $"{path}/duties",
            new { userId = 100, role = "HOST", responsibility = "Introduce speakers" }, "assign-campus-host");
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var duty = await Json(assigned);
        var confirm = $"{path}/duties/{duty.GetProperty("id").GetGuid()}/confirm";
        var input = new { expectedVersion = duty.GetProperty("version").GetGuid() };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(outsider, HttpMethod.Post, confirm, input, "foreign-campus-confirm")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, confirm, input, "early-duty-confirm")).StatusCode);
        var published = await Send(reviewer, HttpMethod.Post, $"{path}/publish", new
        {
            expectedVersion = draft.GetProperty("version").GetGuid(),
            registrationOpensAt = Instant.AddHours(1), registrationClosesAt = Instant.AddDays(3)
        }, "publish-campus-duty");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        f.Clock.Utc = Instant.AddDays(3).AddHours(2);
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed;
            activity.PublicationStatus = "COMPLETED";
            activity.UpdatedAtUtc = f.Clock.Utc;
            await db.SaveChangesAsync();
        });
        var roles = $"/api/activities/{id}/final-roles";
        var before = await reviewer.GetFromJsonAsync<JsonElement>(roles);
        Assert.Empty(before.GetProperty("items")[0].GetProperty("eligibleRoles").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, confirm, input, "confirm-campus-host")).StatusCode);
        var after = await reviewer.GetFromJsonAsync<JsonElement>(roles);
        Assert.Equal("HOST", after.GetProperty("items")[0].GetProperty("eligibleRoles")[0].GetString());
    }

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
        var joinedValue = await Json(joined);
        var application = joinedValue.GetProperty("id").GetGuid();
        Assert.Equal("PENDING", joinedValue.GetProperty("status").GetString());
        Assert.False(joinedValue.TryGetProperty("assignmentId", out _));
        Assert.Equal(application, (await Json(await Send(student, HttpMethod.Post, join, new { expectedVersion = 4 }, "join"))).GetProperty("id").GetGuid());
        await f.WithDb(async db => Assert.Empty(await db.Set<ActivityStaffAssignment>().ToArrayAsync()));
        var mine = await Json(await student.GetAsync($"/api/activities/{activity}/staffing"));
        Assert.Equal("PENDING", mine.GetProperty("recruitment").GetProperty("positions")[0].GetProperty("myApplication").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(student, HttpMethod.Post, join, new { expectedVersion = 5 }, "duplicate")).StatusCode);
        var review = $"/api/activities/{activity}/recruitment/applications/{application}/review";
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(student, HttpMethod.Post, review, new { expectedVersion = 5, applicationVersion = 1, decision = "APPROVED" }, "self-review")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Post, review, new { expectedVersion = 4, applicationVersion = 1, decision = "APPROVED" }, "stale")).StatusCode);
        var decision = new { expectedVersion = 5, applicationVersion = 1, decision = "APPROVED" };
        var accepted = await Send(manager, HttpMethod.Post, review, decision, "approve");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var assignment = (await Json(accepted)).GetProperty("assignmentId").GetGuid();
        Assert.Equal(assignment, (await Json(await Send(manager, HttpMethod.Post, review, decision, "approve"))).GetProperty("assignmentId").GetGuid());
        f.ManagerEnabled = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, HttpMethod.Post, review, decision, "approve")).StatusCode);
        f.ManagerEnabled = true;
        using var second = f.Client(101);
        var outsiderView = await Json(await second.GetAsync($"/api/activities/{activity}/staffing"));
        Assert.Empty(outsiderView.GetProperty("assignments").EnumerateArray());
        Assert.Empty(outsiderView.GetProperty("applications").EnumerateArray());
        Assert.Single(outsiderView.GetProperty("publicAssignments").EnumerateArray());
        Assert.False(outsiderView.GetProperty("publicAssignments")[0].TryGetProperty("userId", out _));
        Assert.False(outsiderView.GetProperty("publicAssignments")[0].TryGetProperty("completions", out _));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(second, HttpMethod.Post, join, new { expectedVersion = 6 }, "seat")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = false, expectedVersion = 2 }, "hide-used")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/assignments/{assignment}/cancel", new { expectedVersion = 6 }, "cancel")).StatusCode);
        var next = await Send(second, HttpMethod.Post, join, new { expectedVersion = 7 }, "seat-after-cancel");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        var nextId = (await Json(next)).GetProperty("id").GetGuid();
        var nextReview = $"/api/activities/{activity}/recruitment/applications/{nextId}/review";
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Post, nextReview, new { expectedVersion = 8, applicationVersion = 1, decision = "REJECTED" }, "reason-required")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, nextReview, new { expectedVersion = 8, applicationVersion = 1, decision = "REJECTED", reason = "Chọn ứng viên khác" }, "reject")).StatusCode);
        await f.WithDb(async db =>
        {
            Assert.Equal(1, await db.Set<ActivityStaffAssignment>().CountAsync());
            Assert.Equal(0, await db.Set<ActivityStaffAssignment>().CountAsync(x => x.Status != "REMOVED"));
            Assert.Equal("REJECTED", (await db.Set<ActivityRecruitmentApplication>().SingleAsync(x => x.Id == nextId)).Status);
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
        var resourceId = Guid.NewGuid();
        await f.WithDb(async db => { db.Add(new ActivityResource { Id = resourceId, ActivityId = activity, Title = "Kết quả", Url = "https://example.test/evidence.pdf", Kind = "DOCUMENT", Visibility = "CLUB", UploadedByUserId = 200, CreatedAtUtc = Instant, UpdatedAtUtc = Instant }); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(student, HttpMethod.Post, $"/api/activities/{activity}/assignments/{assignment}/completions", new
        { expectedVersion = 3, occurrenceKey = "output-1", occurrenceLabel = "First output", evidence = "Delivered", resourceIds = new[] { Guid.NewGuid() } }, "invalid-resource")).StatusCode);
        var completed = await Send(student, HttpMethod.Post, $"/api/activities/{activity}/assignments/{assignment}/completions", new
        { expectedVersion = 3, occurrenceKey = "output-1", occurrenceLabel = "First output", evidence = "Delivered and documented", resourceIds = new[] { resourceId } }, "completion");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode); var completion = (await Json(completed)).GetProperty("id").GetGuid();
        var own = await Json(await student.GetAsync($"/api/activities/{activity}/staffing"));
        Assert.Equal(resourceId, own.GetProperty("assignments")[0].GetProperty("completions")[0].GetProperty("resources")[0].GetProperty("id").GetGuid());
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
        var path = $"/api/activities/{activity}/publication";
        var opens = Instant.AddHours(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(manager, HttpMethod.Patch, path,
            new { isPublished = true, expectedVersion = 1, registrationOpensAt = opens, registrationClosesAt = Instant.AddDays(2) },
            "late-registration-close")).StatusCode);
        var closes = Instant.AddDays(1);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, path,
            new { isPublished = true, expectedVersion = 1, registrationOpensAt = opens, registrationClosesAt = closes },
            "valid-registration-window")).StatusCode);
        var published = await manager.GetFromJsonAsync<JsonElement>($"/api/activities/{activity}");
        Assert.Equal(opens, published.GetProperty("registrationOpensAt").GetDateTimeOffset());
        Assert.Equal(closes, published.GetProperty("registrationClosesAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task SchedulingPublicationKeepsTheActivityPrivate()
    {
        await using var f = await Fixture.Create();
        var (_, activityId) = await f.Approved();
        using var manager = f.Client(200);
        var publishAt = Instant.AddHours(1);
        var response = await Send(manager, HttpMethod.Patch, $"/api/activities/{activityId}/publication",
            new { isPublished = true, expectedVersion = 1, publishAtUtc = publishAt,
                registrationOpensAt = publishAt.AddHours(1), registrationClosesAt = Instant.AddDays(1) },
            "schedule-publication");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scheduled = await Json(response);
        Assert.False(scheduled.GetProperty("isPublished").GetBoolean());
        Assert.Equal(publishAt, scheduled.GetProperty("publishAtUtc").GetDateTimeOffset());

        await f.WithDb(async db =>
        {
            var execution = await db.Set<ActivityExecution>().SingleAsync();
            Assert.False(execution.IsPublished);
            Assert.Equal(publishAt, execution.PublishAtUtc);
            Assert.Equal("DRAFT", (await db.Activities.SingleAsync()).PublicationStatus);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RescheduleKeepsTheApprovedDatesUntilReviewAndExpiresAtFortyEightHours(bool expired)
    {
        await using var f = await Fixture.Create();
        var (_, id) = await f.Approved();
        var originalStart = Instant.AddDays(3);
        Guid version = default;
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.StartTimeUtc = originalStart;
            activity.EndTimeUtc = originalStart.AddHours(2);
            version = activity.Version;
            await db.SaveChangesAsync();
        });
        using var manager = f.Client(200);
        using var reviewer = f.Client(202);
        var path = $"/api/activities/{id}/reschedule-requests";
        var newStart = originalStart.AddDays(1);
        var created = await Send(manager, HttpMethod.Post, path,
            new { expectedActivityVersion = version, startTimeUtc = newStart, endTimeUtc = newStart.AddHours(2), reason = "Venue change" },
            "request-new-dates");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var request = await Json(created);
        var deadline = originalStart.AddDays(-2);
        Assert.Equal(deadline, request.GetProperty("reviewDeadlineUtc").GetDateTimeOffset());
        await f.WithDb(async db => Assert.Equal(originalStart, (await db.Activities.SingleAsync()).StartTimeUtc));

        f.Clock.Utc = expired ? deadline : deadline.AddSeconds(-1);
        var listing = await reviewer.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(expired ? "EXPIRED" : "PENDING", listing.GetProperty("items")[0].GetProperty("status").GetString());
        var reviewed = await Send(reviewer, HttpMethod.Post, $"{path}/{request.GetProperty("id").GetGuid()}/review",
            new { expectedVersion = request.GetProperty("version").GetGuid(), decision = "APPROVED" }, "review-new-dates");
        Assert.Equal(expired ? HttpStatusCode.Conflict : HttpStatusCode.OK, reviewed.StatusCode);
        await f.WithDb(async db => Assert.Equal(expired ? originalStart : newStart, (await db.Activities.SingleAsync()).StartTimeUtc));
    }

    [Fact]
    public async Task RecapOpensOnCompletionAndMembersOnlyReadTheirOwnFeedback()
    {
        await using var f = await Fixture.Create();
        var (_, id) = await f.Approved();
        using var manager = f.Client(200);
        using var member = f.Client(100);
        using var outsider = f.Client(101);
        var path = $"/api/activities/{id}/recap";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(member, HttpMethod.Put, $"{path}/my-feedback",
            new { strengths = "Too early" }, "recap-too-early")).StatusCode);
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed;
            activity.PublicationStatus = "COMPLETED";
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(path)).StatusCode);
        var saved = await Send(member, HttpMethod.Put, $"{path}/my-feedback",
            new { strengths = "Good facilitation" }, "member-recap");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await Json(saved)).GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Put, $"{path}/my-feedback",
            new { weaknesses = "Manager-only example" }, "manager-recap")).StatusCode);
        var own = await member.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("Good facilitation", own.GetProperty("myFeedback").GetProperty("strengths").GetString());
        Assert.Empty(own.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("Manager-only example", own.GetRawText());
        Assert.Equal(2, (await manager.GetFromJsonAsync<JsonElement>(path)).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await Send(member, HttpMethod.Put, $"{path}/my-feedback",
            new { expectedVersion = version, suggestions = "Add a break" }, "member-recap-edit")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(member, HttpMethod.Put, $"{path}/my-feedback",
            new { expectedVersion = version, suggestions = "Stale edit" }, "member-recap-stale")).StatusCode);
    }

    [Fact]
    public async Task ClosingReportUsesTheCampusPolicyAtCompletionAndApprovalClosesRecap()
    {
        await using var f = await Fixture.Create();
        var (_, id) = await f.Approved();
        using var manager = f.Client(200);
        using var reviewer = f.Client(202);
        using var member = f.Client(100);
        const string policyPath = "/api/activities/closing-policy/1";
        var reportPath = $"/api/activities/{id}/closing-report";
        var recapPath = $"/api/activities/{id}/recap";
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(policyPath)).StatusCode);
        var policy = await Send(reviewer, HttpMethod.Put, policyPath, new { reportDays = 4, expectedVersion = 0 }, "set-report-days");
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        var policyVersion = (await Json(policy)).GetProperty("version").GetInt64();
        f.Clock.Utc = new DateTimeOffset(2026, 11, 10, 14, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed;
            activity.PublicationStatus = "COMPLETED";
            activity.UpdatedAtUtc = f.Clock.Utc;
            await db.SaveChangesAsync();
        });
        var expectedDeadline = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        f.Clock.Utc = f.Clock.Utc.AddDays(1);
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Put, policyPath,
            new { reportDays = 1, expectedVersion = policyVersion }, "change-report-days")).StatusCode);
        var state = await manager.GetFromJsonAsync<JsonElement>(reportPath);
        Assert.Equal(expectedDeadline, state.GetProperty("deadlineUtc").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.OK, (await Send(member, HttpMethod.Put, $"{recapPath}/my-feedback",
            new { strengths = "A useful workshop" }, "recap-before-approval")).StatusCode);
        var submitted = await Send(manager, HttpMethod.Post, reportPath, new { summary = "Workshop completed" }, "submit-closing-report");
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var version = (await Json(submitted)).GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, HttpMethod.Post, $"{reportPath}/approve",
            new { expectedVersion = version }, "approve-closing-report")).StatusCode);
        Assert.False((await member.GetFromJsonAsync<JsonElement>(recapPath)).GetProperty("canSubmit").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(member, HttpMethod.Put, $"{recapPath}/my-feedback",
            new { strengths = "Too late" }, "recap-after-approval")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(reviewer, HttpMethod.Post, $"{reportPath}/approve",
            new { expectedVersion = version }, "approve-again")).StatusCode);
    }

    [Fact]
    public async Task FinalRoleRequiresVerificationAndRemainsLockedAfterTheReportDeadline()
    {
        await using var f = await Fixture.Create();
        var (_, id) = await f.Approved();
        using var manager = f.Client(200);
        using var reviewer = f.Client(202);
        var path = $"/api/activities/{id}/final-roles";
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed;
            activity.UpdatedAtUtc = Instant;
            db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = id, UserId = 100, FullName = "Student 100" });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Put, $"{path}/100",
            new { role = "PARTICIPANT" }, "unverified-role")).StatusCode);
        await f.WithDb(async db =>
        {
            db.ActivityAttendances.Add(new ActivityAttendance
            {
                ActivityId = id, UserId = 100, FullName = "Student 100", VerificationStatus = "VERIFIED",
                AttendanceDate = DateOnly.FromDateTime(Instant.Date)
            });
            await db.SaveChangesAsync();
        });
        var candidates = await manager.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("PARTICIPANT", candidates.GetProperty("items")[0].GetProperty("eligibleRoles")[0].GetString());
        var confirmed = await Send(manager, HttpMethod.Put, $"{path}/100", new { role = "PARTICIPANT" }, "verified-role");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var roleVersion = (await Json(confirmed)).GetProperty("version").GetGuid();
        f.Clock.Utc = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        var locked = await reviewer.GetFromJsonAsync<JsonElement>(path);
        Assert.True(locked.GetProperty("locked").GetBoolean());
        Assert.Equal("PARTICIPANT", locked.GetProperty("items")[0].GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(manager, HttpMethod.Put, $"{path}/100",
            new { role = "PARTICIPANT", expectedVersion = roleVersion }, "late-role")).StatusCode);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task ClosingReportApprovalDependsOnSubmissionTimeEvenWhenReviewedAfterDeadline(int secondsFromDeadline, bool canApprove)
    {
        await using var f = await Fixture.Create();
        var (_, id) = await f.Approved();
        using var manager = f.Client(200);
        using var reviewer = f.Client(202);
        var path = $"/api/activities/{id}/closing-report";
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync();
            activity.Status = ActivityStatuses.Completed;
            activity.UpdatedAtUtc = Instant;
            await db.SaveChangesAsync();
        });
        var state = await manager.GetFromJsonAsync<JsonElement>(path);
        var deadline = state.GetProperty("deadlineUtc").GetDateTimeOffset();
        f.Clock.Utc = deadline.AddSeconds(secondsFromDeadline);
        var submission = await Send(manager, HttpMethod.Post, path, new { summary = "Workshop completed" }, "deadline-submission");
        Assert.Equal(HttpStatusCode.OK, submission.StatusCode);
        var version = (await Json(submission)).GetProperty("version").GetGuid();
        f.Clock.Utc = deadline.AddHours(1);
        var reviewState = await reviewer.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(canApprove, reviewState.GetProperty("canApprove").GetBoolean());
        Assert.Equal(!canApprove, reviewState.GetProperty("isOverdue").GetBoolean());
        var approval = await Send(reviewer, HttpMethod.Post, $"{path}/approve",
            new { expectedVersion = version }, "deadline-approval");
        Assert.Equal(canApprove ? HttpStatusCode.OK : HttpStatusCode.Conflict, approval.StatusCode);
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
        var first = await Send(one, HttpMethod.Post, url, new { expectedVersion = 4 }, "one");
        var second = await Send(two, HttpMethod.Post, url, new { expectedVersion = 5 }, "two");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstId = (await Json(first)).GetProperty("id").GetGuid(); var secondId = (await Json(second)).GetProperty("id").GetGuid();
        await f.WithDb(async db => Assert.Empty(await db.Set<ActivityStaffAssignment>().ToArrayAsync()));
        var results = await Task.WhenAll(Send(manager, HttpMethod.Post, $"/api/activities/{activity}/recruitment/applications/{firstId}/review", new { expectedVersion = 6, applicationVersion = 1, decision = "APPROVED" }, "approve-one"),
            Send(manager, HttpMethod.Post, $"/api/activities/{activity}/recruitment/applications/{secondId}/review", new { expectedVersion = 6, applicationVersion = 1, decision = "APPROVED" }, "approve-two"));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var list = await one.GetAsync("/api/clubs/1/activity-recruitments?from=2026-10-22T00:00:00Z&to=2026-10-30T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode); Assert.Single((await Json(list)).GetProperty("items").EnumerateArray());
        await f.WithDb(async db =>
        {
            Assert.Equal(2, await db.Activities.CountAsync()); Assert.Equal("Legacy row", (await db.Activities.SingleAsync(x => x.Title == "Legacy row")).Title);
            Assert.Equal(1, await db.Set<ActivityStaffAssignment>().CountAsync(x => x.ActivityId == activity));
            Assert.Equal(2, await db.Set<ActivityRecruitmentApplication>().CountAsync());
            Assert.Single(await db.Set<ActivityExecutionAudit>().Where(x => x.Action == "RECRUITMENT_REVIEWED").ToArrayAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task Recruitment_HostReviewsCurrentEligibleApplicantButStaffAndVerificationGrantCannotReview()
    {
        await using var f = await Fixture.Create(); var (_, activity) = await f.Approved();
        using var manager = f.Client(200); using var host = f.Client(100); using var applicant = f.Client(101); using var verifier = f.Client(300);
        var staffing = await Json(await manager.GetAsync($"/api/activities/{activity}/staffing"));
        var rule = staffing.GetProperty("actions")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/publication", new { isPublished = true, expectedVersion = 1 }, "publish")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/recruitment", new
        { expectedVersion = 2, title = "Staff", visibility = "PUBLIC", eligibility = "SCHOOL_STUDENTS", positions = new[] { new { actionRuleId = rule, capacity = 1, assignmentKind = "STAFF" } } }, "recruit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Patch, $"/api/activities/{activity}/recruitment/status", new { expectedVersion = 3, status = "OPEN" }, "open")).StatusCode);
        var assigned = await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/assignments", new { expectedVersion = 4, userId = 100, actionRuleId = rule, assignmentKind = "HOST" }, "host");
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        staffing = await Json(await applicant.GetAsync($"/api/activities/{activity}/staffing"));
        var position = staffing.GetProperty("recruitment").GetProperty("positions")[0].GetProperty("id").GetGuid();
        var applied = await Send(applicant, HttpMethod.Post, $"/api/activities/{activity}/recruitment/positions/{position}/join", new { expectedVersion = 5 }, "apply");
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var application = (await Json(applied)).GetProperty("id").GetGuid();
        var review = $"/api/activities/{activity}/recruitment/applications/{application}/review";
        var decision = new { expectedVersion = 6, applicationVersion = 1, decision = "APPROVED" };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(verifier, HttpMethod.Post, review, decision, "not-event-reviewer")).StatusCode);
        f.ApplicantEligible = false;
        Assert.Equal(HttpStatusCode.Conflict, (await Send(host, HttpMethod.Post, review, decision, "locked-account")).StatusCode);
        await f.WithDb(async db => Assert.Equal("PENDING", (await db.Set<ActivityRecruitmentApplication>().SingleAsync()).Status));
        f.ApplicantEligible = true;
        Assert.Equal(HttpStatusCode.OK, (await Send(host, HttpMethod.Post, review, decision, "host-review")).StatusCode);
        var hostId = (await Json(assigned)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activities/{activity}/assignments/{hostId}/cancel", new { expectedVersion = 7 }, "remove-host")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(host, HttpMethod.Post, review, decision, "host-review")).StatusCode);
    }

    private sealed class Fixture(SqliteConnection? connection, WebApplication app, string? sqlConnection) : IAsyncDisposable
    {
        internal bool ManagerEnabled { get; set; } = true;
        internal bool VerifierEnabled { get; set; } = true;
        internal bool AuthorityAvailable { get; set; } = true;
        internal bool SemesterActive { get; set; } = true;
        internal bool AttendanceVerification { get; set; } = true;
        internal bool ApplicantEligible { get; set; } = true;
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
                    result = ids.Select(id => new InternalUserProfile(id, "User " + id, id + "@example.test", id == 203 ? "HCM" : "HN", id != 101 || f.ApplicantEligible, false, null)).ToArray();
                }
                else if (path == "/api/internal/experience/clubs/1/manager-ids")
                    result = new[] { 200 };
                else if (path == "/api/internal/experience/clubs/1/members-at")
                {
                    var membership = await r.Content!.ReadFromJsonAsync<ClubMembersAtRequest>();
                    Assert.Equal(Instant.AddDays(1), membership!.AtUtc);
                    result = new[] { 100 };
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
                else if (path == $"/api/internal/experience/policies/{CampusPolicyVersion}")
                    result = new ExperiencePolicyReference(CampusPolicyVersion, "V1", "FA2026", Instant.AddDays(-30),
                        Instant.AddDays(60), "APPROVED", "PROJECT", 2, "REVERSE_SOURCE_INVALIDATION");
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
                        // Seed only columns that existed at the historical migration boundary.
                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            INSERT INTO Activities
                                (ClubId, ClubName, Title, Description, Location, StartTimeUtc, EndTimeUtc,
                                 MeetingDaysCsv, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc)
                            VALUES (1, N'Legacy', N'Legacy row', N'Preserved', N'Hall',
                                {Instant.AddDays(-2)}, {Instant.AddDays(-1)}, N'', {ActivityStatuses.Scheduled},
                                200, {Instant}, {Instant})
                            """);
                    }
                    await db.Database.MigrateAsync();
                    if (upgrade) Assert.True((await db.Activities.SingleAsync()).IsInternal);
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
