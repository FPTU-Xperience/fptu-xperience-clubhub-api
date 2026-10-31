using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class ActivityActionContractTests
{
    [Fact]
    public async Task Application_ListUsesOneExecutionQueryAndMatchesDetailForMixedDraftAndApprovedPages()
    {
        await using var f = await Fixture.Create(sql: true);
        var (approvedId, activityId) = await f.Approved();
        using var manager = f.Client(200);
        for (var index = 0; index < 8; index++)
            Assert.Equal(HttpStatusCode.Created, (await Send(manager, HttpMethod.Post, "/api/activity-applications",
                Payload(title: "Draft " + index), "list-draft-" + index)).StatusCode);

        string databaseName = "";
        string executionTable = "";
        await f.WithDb(async db =>
        {
            databaseName = db.Database.GetDbConnection().Database;
            executionTable = db.Model.FindEntityType(typeof(ActivityExecution))!.GetTableName()!;
            db.Add(new ActivityApplication
            {
                ClubId = 2,
                CampusCode = "HN",
                SemesterId = "FA2026",
                CreatedBy = 201,
                ContentJson = JsonSerializer.Serialize(Payload(title: "Other club private draft")),
                CreatedAt = Instant,
                UpdatedAt = Instant
            });
            await db.SaveChangesAsync();
        });

        using var queries = new ExecutionQueryCounter(databaseName, executionTable);
        var response = await manager.GetAsync("/api/activity-applications?clubId=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await Json(response);
        Assert.Equal(1, queries.Count);
        Assert.Equal(9, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(9, items.Length);
        Assert.DoesNotContain("Other club private draft", page.GetRawText());
        Assert.Equal(items.Select(x => x.GetProperty("id").GetInt32()).OrderByDescending(x => x),
            items.Select(x => x.GetProperty("id").GetInt32()));
        var approved = Assert.Single(items, x => x.GetProperty("id").GetInt32() == approvedId);
        Assert.Equal(activityId, approved.GetProperty("activityId").GetInt32());
        Assert.Equal("APPROVED", approved.GetProperty("status").GetString());
        Assert.All(items.Where(x => x.GetProperty("id").GetInt32() != approvedId), item =>
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty("activityId").ValueKind);
            Assert.Equal("DRAFT", item.GetProperty("status").GetString());
            Assert.True(item.GetProperty("capabilities").GetProperty("canEdit").GetBoolean());
        });
        foreach (var item in items)
        {
            var detail = await Json(await manager.GetAsync("/api/activity-applications/" + item.GetProperty("id").GetInt32()));
            Assert.Equal(detail.GetRawText(), item.GetRawText());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("expectedRawXp").ValueKind);
            Assert.False(item.GetProperty("canAwardPoints").GetBoolean());
        }

        queries.Reset();
        var filtered = await Json(await manager.GetAsync("/api/activity-applications?clubId=1&status=APPROVED&pageSize=1"));
        Assert.Equal(1, queries.Count);
        Assert.Equal(approvedId, Assert.Single(filtered.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
        queries.Reset();
        var empty = await Json(await manager.GetAsync("/api/activity-applications?clubId=1&page=99&pageSize=20"));
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(0, queries.Count);
        f.ManagerEnabled = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/activity-applications?clubId=1")).StatusCode);
    }

    [Fact]
    public async Task Staffing_ReadKeepsPerActionOccupancyCompletionCountsAndEvidencePrivateAcrossActors()
    {
        await using var f = await Fixture.Create();
        var (_, activityId) = await f.Approved();
        int workRuleId = 0;
        int attendanceRuleId = 0;
        Guid workPositionId = Guid.Empty;
        Guid attendancePositionId = Guid.Empty;
        Guid ownWorkId = Guid.Empty;
        Guid removedWorkId = Guid.Empty;
        Guid otherAttendanceId = Guid.Empty;
        await f.WithDb(async db =>
        {
            var activity = await db.Activities.SingleAsync(x => x.Id == activityId);
            activity.PublicationStatus = "PUBLISHED";
            var execution = await db.Set<ActivityExecution>().SingleAsync(x => x.ActivityId == activityId);
            execution.IsPublished = true;
            var work = await db.Set<ActivityActionRule>().SingleAsync(x => x.ActivityId == activityId);
            workRuleId = work.Id;
            var definition = JsonSerializer.Deserialize<ActivityActionInput>(work.DefinitionJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var attendance = new ActivityActionRule
            {
                ActivityId = activityId,
                ClientId = "arrival",
                DefinitionJson = JsonSerializer.Serialize(definition with
                { ClientId = "arrival", Name = "Arrival attendance", VerificationMethod = "ATTENDANCE", ExpectedPeople = 3, MaxAwardCount = 3 },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
            db.Add(attendance);
            await db.SaveChangesAsync();
            attendanceRuleId = attendance.Id;
            var recruitment = new ActivityRecruitment
            { ActivityId = activityId, Title = "Public positions", Visibility = "PUBLIC", Eligibility = "SCHOOL_STUDENTS", Status = "OPEN" };
            var workPosition = new ActivityRecruitmentPosition
            { RecruitmentId = recruitment.Id, ActionRuleId = workRuleId, Capacity = 2 };
            var attendancePosition = new ActivityRecruitmentPosition
            { RecruitmentId = recruitment.Id, ActionRuleId = attendanceRuleId, Capacity = 1 };
            var ownWork = new ActivityStaffAssignment
            { ActivityId = activityId, ActionRuleId = workRuleId, PositionId = workPosition.Id, UserId = 100, DisplayName = "Worker 100", CreatedBy = 200, CreatedAt = Instant };
            var removedWork = new ActivityStaffAssignment
            { ActivityId = activityId, ActionRuleId = workRuleId, PositionId = workPosition.Id, UserId = 101, DisplayName = "Removed worker 101", Status = "REMOVED", CreatedBy = 200, CreatedAt = Instant };
            var otherAttendance = new ActivityStaffAssignment
            { ActivityId = activityId, ActionRuleId = attendanceRuleId, PositionId = attendancePosition.Id, UserId = 101, DisplayName = "Arrival 101", CreatedBy = 200, CreatedAt = Instant };
            db.AddRange(recruitment, workPosition, attendancePosition, ownWork, removedWork, otherAttendance);
            db.AddRange(
                new ActivityActionCompletion
                { ActivityId = activityId, AssignmentId = ownWork.Id, ActionRuleId = workRuleId, UserId = 100, OccurrenceKey = "work", OccurrenceLabel = "Work delivered", Evidence = "PRIVATE WORK 100", Status = "VERIFIED", SubmittedAt = Instant },
                new ActivityActionCompletion
                { ActivityId = activityId, AssignmentId = removedWork.Id, ActionRuleId = workRuleId, UserId = 101, OccurrenceKey = "removed", OccurrenceLabel = "Removed output", Evidence = "PRIVATE REMOVED 101", SubmittedAt = Instant },
                new ActivityActionCompletion
                { ActivityId = activityId, AssignmentId = otherAttendance.Id, ActionRuleId = attendanceRuleId, UserId = 101, OccurrenceKey = "arrival", OccurrenceLabel = "Arrival claim", Evidence = "PRIVATE ARRIVAL 101", SubmittedAt = Instant });
            await db.SaveChangesAsync();
            workPositionId = workPosition.Id;
            attendancePositionId = attendancePosition.Id;
            ownWorkId = ownWork.Id;
            removedWorkId = removedWork.Id;
            otherAttendanceId = otherAttendance.Id;
        });

        var path = $"/api/activities/{activityId}/staffing";
        using var manager = f.Client(200);
        var management = await Json(await manager.GetAsync(path));
        Assert.Equal(2, management.GetProperty("actions").GetArrayLength());
        var managed = management.GetProperty("assignments").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid());
        Assert.Equal(3, managed.Count);
        Assert.Equal(1, managed[ownWorkId].GetProperty("verifiedOccurrences").GetInt32());
        Assert.Equal(0, managed[otherAttendanceId].GetProperty("verifiedOccurrences").GetInt32());
        Assert.Equal("Produce output", managed[ownWorkId].GetProperty("actionName").GetString());
        Assert.Equal("Arrival attendance", managed[otherAttendanceId].GetProperty("actionName").GetString());
        Assert.False(managed[removedWorkId].GetProperty("canRemove").GetBoolean());
        Assert.DoesNotContain("PRIVATE ", management.GetRawText());
        var positions = management.GetProperty("recruitment").GetProperty("positions").EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetGuid());
        Assert.Equal(1, positions[workPositionId].GetProperty("filledCount").GetInt32());
        Assert.True(positions[workPositionId].GetProperty("canJoin").GetBoolean());
        Assert.Equal(1, positions[attendancePositionId].GetProperty("filledCount").GetInt32());
        Assert.False(positions[attendancePositionId].GetProperty("canJoin").GetBoolean());

        using var worker = f.Client(100);
        var self = await Json(await worker.GetAsync(path));
        var selfAssignment = Assert.Single(self.GetProperty("assignments").EnumerateArray());
        Assert.Equal(ownWorkId, selfAssignment.GetProperty("id").GetGuid());
        Assert.Contains("PRIVATE WORK 100", self.GetRawText());
        Assert.DoesNotContain("PRIVATE ARRIVAL 101", self.GetRawText());
        Assert.DoesNotContain("PRIVATE REMOVED 101", self.GetRawText());
        Assert.Empty(self.GetProperty("eligibleAssignees").EnumerateArray());

        using var secondWorker = f.Client(101);
        var second = await Json(await secondWorker.GetAsync(path));
        Assert.Equal(2, second.GetProperty("assignments").GetArrayLength());
        var secondPositions = second.GetProperty("recruitment").GetProperty("positions").EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, secondPositions[workPositionId].GetProperty("myAssignmentId").ValueKind);
        Assert.True(secondPositions[workPositionId].GetProperty("canJoin").GetBoolean());
        Assert.Equal(otherAttendanceId, secondPositions[attendancePositionId].GetProperty("myAssignmentId").GetGuid());
        Assert.DoesNotContain("PRIVATE WORK 100", second.GetRawText());

        using var verifier = f.Client(300);
        var verification = await Json(await verifier.GetAsync(path));
        var reviewable = verification.GetProperty("assignments").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid());
        Assert.True(Assert.Single(reviewable[otherAttendanceId].GetProperty("completions").EnumerateArray()).GetProperty("canVerify").GetBoolean());
        Assert.False(Assert.Single(reviewable[removedWorkId].GetProperty("completions").EnumerateArray()).GetProperty("canVerify").GetBoolean());
        Assert.Contains("PRIVATE ARRIVAL 101", verification.GetRawText());
        Assert.False(verification.GetProperty("canAwardPoints").GetBoolean());
        f.ManagerEnabled = false;
        var revoked = await Json(await manager.GetAsync(path));
        Assert.Empty(revoked.GetProperty("assignments").EnumerateArray());
        Assert.False(revoked.GetProperty("capabilities").GetProperty("canManageAssignments").GetBoolean());
        Assert.DoesNotContain("PRIVATE ", revoked.GetRawText());
    }

    private sealed class ExecutionQueryCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string databaseName;
        private readonly string tableName;
        private readonly List<IDisposable> subscriptions = [];
        private readonly IDisposable allListeners;
        private int count;

        internal ExecutionQueryCounter(string databaseName, string tableName)
        {
            this.databaseName = databaseName;
            this.tableName = tableName;
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        internal int Count => Volatile.Read(ref count);
        internal void Reset() => Interlocked.Exchange(ref count, 0);
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                lock (subscriptions) subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == RelationalEventId.CommandExecuted.Name && value.Value is CommandExecutedEventData executed
                && executed.Command.Connection?.Database == databaseName
                && executed.Command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && executed.Command.CommandText.Contains(tableName, StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref count);
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            allListeners.Dispose();
            lock (subscriptions)
                foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Student_DiscoveryKeepsEarliestSessionTieBreakAndOnlyUsesOwnRegistrationAndVerification()
    {
        await using var app = await CreateStudentActivityAppAsync();
        var now = DateTimeOffset.UtcNow;
        int checkInId;
        int verifiedId;
        int registrationId;
        int closedId;
        int firstSessionId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var checkIn = StudentActivity("Self check-in");
            var verified = StudentActivity("Self verified");
            var register = StudentActivity("Can register");
            var closed = StudentActivity("Registration closed");
            closed.RegistrationClosesAt = now.AddMinutes(-10);
            db.Activities.AddRange(checkIn, verified, register, closed);
            await db.SaveChangesAsync();
            var first = new ActivitySession
            { ActivityId = checkIn.Id, SemesterId = "FA26", Location = "First session", StartTimeUtc = now.AddMinutes(-10), EndTimeUtc = now.AddHours(1), OpensAt = now.AddMinutes(-15), ClosesAt = now.AddMinutes(50) };
            var tied = new ActivitySession
            { ActivityId = checkIn.Id, SemesterId = "FA26", Location = "Later session ID", StartTimeUtc = first.StartTimeUtc, EndTimeUtc = first.EndTimeUtc, OpensAt = first.OpensAt, ClosesAt = first.ClosesAt };
            var verifiedSession = new ActivitySession
            { ActivityId = verified.Id, SemesterId = "FA26", Location = "Verified session", StartTimeUtc = now.AddMinutes(-5), EndTimeUtc = now.AddHours(1), OpensAt = now.AddMinutes(-15), ClosesAt = now.AddMinutes(50) };
            db.Sessions.Add(first);
            await db.SaveChangesAsync();
            db.Sessions.AddRange(tied, verifiedSession);
            await db.SaveChangesAsync();
            db.ActivityParticipants.AddRange(
                new ActivityParticipant { ActivityId = checkIn.Id, UserId = 104, FullName = "Self participant" },
                new ActivityParticipant { ActivityId = verified.Id, UserId = 104, FullName = "Self participant" },
                new ActivityParticipant { ActivityId = register.Id, UserId = 105, FullName = "PRIVATE OTHER PARTICIPANT" });
            db.ActivityAttendances.AddRange(
                new ActivityAttendance { ActivityId = checkIn.Id, SessionId = first.Id, UserId = 105, FullName = "PRIVATE OTHER VERIFIED", AttendanceDate = DateOnly.FromDateTime(now.Date), VerificationStatus = "VERIFIED" },
                new ActivityAttendance { ActivityId = checkIn.Id, SessionId = tied.Id, UserId = 104, AttendanceDate = DateOnly.FromDateTime(now.Date), VerificationStatus = "VERIFIED" },
                new ActivityAttendance { ActivityId = verified.Id, SessionId = verifiedSession.Id, UserId = 104, AttendanceDate = DateOnly.FromDateTime(now.Date), VerificationStatus = "VERIFIED" });
            await db.SaveChangesAsync();
            checkInId = checkIn.Id;
            verifiedId = verified.Id;
            registrationId = register.Id;
            closedId = closed.Id;
            firstSessionId = first.Id;
        }

        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/feed?pageSize=10");
        Assert.Equal(4, page.GetProperty("totalItems").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetInt32());
        Assert.Equal(firstSessionId, items[checkInId].GetProperty("nextSessionId").GetInt32());
        Assert.Equal("First session", items[checkInId].GetProperty("location").GetString());
        Assert.Equal("CHECK_IN", items[checkInId].GetProperty("nextAction").GetString());
        Assert.Equal("VIEW", items[verifiedId].GetProperty("nextAction").GetString());
        Assert.Equal("REGISTER", items[registrationId].GetProperty("nextAction").GetString());
        Assert.True(items[registrationId].GetProperty("canRegister").GetBoolean());
        Assert.Equal("VIEW", items[closedId].GetProperty("nextAction").GetString());
        Assert.False(items[closedId].GetProperty("canRegister").GetBoolean());
        Assert.DoesNotContain("PRIVATE OTHER", page.GetRawText());
        Assert.DoesNotContain("participants", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        foreach (var item in items.Values)
        {
            var detail = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/" + item.GetProperty("id").GetInt32());
            Assert.Equal(item.GetRawText(), detail.GetRawText());
        }
    }
}
