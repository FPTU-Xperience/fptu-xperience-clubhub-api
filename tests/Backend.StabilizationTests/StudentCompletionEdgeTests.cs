using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Student_QuestJoinRejectsChangesMadeWhileSourceEligibilityIsBeingResolved()
    {
        var state = new StudentSourceState();
        await using var app = await CreateStudentAdminAppAsync(state);
        Guid questId;
        await using (var scope = app.Services.CreateAsyncScope())
            questId = await SeedStudentConfiguredQuestAsync(scope.ServiceProvider.GetRequiredService<AdminDbContext>());
        state.CandidateObserver = query =>
        {
            Assert.True(query.IncludeRegistered);
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            var quest = db.Quests.Single(x => x.Id == questId);
            quest.Status = "paused"; quest.Version = Guid.NewGuid(); db.SaveChanges();
        };
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var result = await client.PostAsJsonAsync($"/api/v1/quests/{questId}/join", new JoinQuestRequest(null));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("STALE_QUEST", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var verify = app.Services.CreateAsyncScope();
        Assert.Equal(0, await verify.ServiceProvider.GetRequiredService<AdminDbContext>().QuestParticipants.CountAsync());
    }

    [Fact]
    public async Task Student_QuestCompletionChecksForeignProofLiveCancellationAndSourceOutageBeforeReplay()
    {
        var state = new StudentSourceState();
        await using var app = await CreateStudentAdminAppAsync(state);
        Guid questId;
        await using (var scope = app.Services.CreateAsyncScope())
            questId = await SeedStudentConfiguredQuestAsync(scope.ServiceProvider.GetRequiredService<AdminDbContext>());
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var joined = await client.PostAsJsonAsync($"/api/v1/quests/{questId}/join", new JoinQuestRequest(null));
        Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        var participation = await joined.Content.ReadFromJsonAsync<QuestParticipantResponse>(ExperienceJson);
        Guid own, foreign;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            own = await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 60);
            foreign = await SeedStudentContributionAsync(db, 105, "FA26", "Academic", 60);
        }
        var request = new CompleteQuestRequest(null, null) { ExpectedVersion = participation!.Version };
        var invalid = await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request with { LinkedContributionIds = [own, foreign] });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "live-source-completion");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request)).StatusCode);
        state.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request)).StatusCode);
        var unavailable = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests/me?page=1");
        Assert.False(unavailable.GetProperty("items")[0].GetProperty("progressAvailable").GetBoolean());
        Assert.Equal("UNAVAILABLE", unavailable.GetProperty("items")[0].GetProperty("effectiveStatus").GetString());
        state.Unavailable = false; state.CancelledSources.Add(new(1, 1));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/quests/{questId}/complete", request)).StatusCode);
        var cancelled = await client.GetFromJsonAsync<JsonElement>("/api/v1/quests/me?page=1");
        Assert.Equal("REQUIRES_REVALIDATION", cancelled.GetProperty("items")[0].GetProperty("effectiveStatus").GetString());
        await using var verify = app.Services.CreateAsyncScope();
        var final = verify.ServiceProvider.GetRequiredService<AdminDbContext>();
        Assert.Equal(2, await final.ExperienceLedgerEffects.CountAsync());
        Assert.Equal(1, await final.QuestCompletionRecords.CountAsync());
        Assert.Equal("Completed", (await final.QuestParticipants.SingleAsync()).Status);
    }

    [Fact]
    public async Task Student_ContributionDetailReconstructsHistoricalDeltasFromTheirFrozenRevisionAndPolicy()
    {
        await using var app = await CreateStudentAdminAppAsync();
        Guid contributionId, originalPolicyId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            contributionId = await SeedStudentContributionAsync(db, 104, "FA26", "Academic", 60);
            originalPolicyId = (await db.ApprovedExperiencePolicies.SingleAsync()).Id;
            var attributes = new ContributionAttributes("T3", "Leader", "Club", "None", [new("Academic", 1)]);
            var upgraded = new ContributionRevision
            {
                ContributionId = contributionId,
                Ordinal = 2,
                Status = "APPROVED",
                AttributesJson = JsonSerializer.Serialize(attributes, ExperienceJson),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.ContributionRevisions.Add(upgraded);
            var points = ExperienceScoringCalculator.CalculateRawPoints("T3", "Leader", "Club", "None");
            var effect = new ExperienceLedgerEffect
            {
                ContributionId = contributionId,
                RevisionId = upgraded.Id,
                UserId = 104,
                SemesterId = "FA26",
                Amount = points - 60,
                EffectType = "UPGRADE_DELTA",
                PolicyVersion = originalPolicyId,
                FormulaVersion = "P_B_R_Q_K_V1",
                RecordedAt = DateTimeOffset.UtcNow,
                VerificationId = Guid.NewGuid()
            };
            var row = await db.Contributions.SingleAsync(x => x.Id == contributionId);
            row.RawPoints = points; row.EffectiveAttributesJson = upgraded.AttributesJson; row.CurrentRevisionId = upgraded.Id;
            db.ExperienceLedgerEffects.Add(effect); await db.SaveChangesAsync();
            db.ExperienceLedgerAllocations.Add(new ExperienceLedgerAllocation { EffectId = effect.Id, Pillar = "Academic", Amount = points - 60 });
            await db.SaveChangesAsync();
            db.ExperienceLedgerEffects.Add(new ExperienceLedgerEffect
            {
                ContributionId = contributionId,
                RevisionId = upgraded.Id,
                UserId = 104,
                SemesterId = "FA26",
                EffectType = "LEGACY_UNSUPPORTED",
                PolicyVersion = originalPolicyId,
                FormulaVersion = "UNKNOWN_V0",
                RecordedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var other = CreateAdminContractClient(app, 105, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/contributions/{contributionId}")).StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/contributions/{contributionId}");
        var history = detail.GetProperty("ledgerEffects");
        Assert.Equal(3, history.GetArrayLength());
        Assert.Equal(60, history[0].GetProperty("calculationBreakdown").GetProperty("rawPoints").GetDecimal());
        Assert.Equal(originalPolicyId, history[0].GetProperty("calculationBreakdown").GetProperty("policyVersion").GetGuid());
        Assert.Equal(60, history[1].GetProperty("beforeRawPoints").GetDecimal());
        Assert.Equal(240, history[1].GetProperty("calculationBreakdown").GetProperty("rawPoints").GetDecimal());
        Assert.Equal(180, history[1].GetProperty("deltaAmount").GetDecimal());
        Assert.Equal("RECONSTRUCTED_FROM_IMMUTABLE_LEDGER_AND_REVISION", history[0].GetProperty("provenance").GetString());
        Assert.False(history[2].GetProperty("calculationAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, history[2].GetProperty("calculationBreakdown").ValueKind);
    }

    [ClubManagerSqlFact]
    public async Task Student_SqlServerQuestSourcesIncludeExistingRegistrationAndRejectCancelledOrMismatchedSessions()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        await using var app = await CreateStudentActivityAppAsync(database.ConnectionString);
        int activityId, sessionId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Full registered quest source");
            activity.Capacity = 1; activity.RegisteredCount = 1; activity.RegistrationClosesAt = DateTimeOffset.UtcNow.AddHours(-1);
            activity.PublicationStatus = "CLOSED";
            db.Activities.Add(activity); await db.SaveChangesAsync(); activityId = activity.Id;
            var session = new ActivitySession
            {
                ActivityId = activityId,
                SemesterId = "FA26",
                StartTimeUtc = activity.StartTimeUtc,
                EndTimeUtc = activity.EndTimeUtc,
                Status = "SCHEDULED"
            };
            db.Sessions.Add(session); db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = activityId, UserId = 104 });
            await db.SaveChangesAsync(); sessionId = session.Id;
        }
        using var internalClient = app.GetTestClient();
        var proofs = new StudentSourceStatusQuery([new(activityId, sessionId), new(activityId + 1, sessionId)]);
        Assert.Equal(HttpStatusCode.Forbidden, (await internalClient.PostAsJsonAsync("/api/internal/experience/student/source-status", proofs)).StatusCode);
        internalClient.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", TestInternalWorkflowToken);
        var normal = await internalClient.PostAsJsonAsync("/api/internal/experience/student/candidates", new StudentCandidateQuery(104));
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        Assert.Empty((await normal.Content.ReadFromJsonAsync<StudentActivityPage>())!.Items);
        var forQuest = await internalClient.PostAsJsonAsync("/api/internal/experience/student/candidates", new StudentCandidateQuery(104, IncludeRegistered: true));
        Assert.Equal(HttpStatusCode.OK, forQuest.StatusCode);
        var candidate = Assert.Single((await forQuest.Content.ReadFromJsonAsync<StudentActivityPage>())!.Items);
        Assert.Equal(activityId, candidate.Id); Assert.False(candidate.CanRegister);
        var valid = await internalClient.PostAsJsonAsync("/api/internal/experience/student/source-status", proofs);
        Assert.Equal(new StudentSourceReference(activityId, sessionId), Assert.Single((await valid.Content.ReadFromJsonAsync<StudentSourceStatus>())!.ActiveSources));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            (await db.Sessions.SingleAsync(x => x.Id == sessionId)).Status = "CANCELLED"; await db.SaveChangesAsync();
        }
        var revoked = await internalClient.PostAsJsonAsync("/api/internal/experience/student/source-status", proofs);
        Assert.Empty((await revoked.Content.ReadFromJsonAsync<StudentSourceStatus>())!.ActiveSources);
    }

    private static async Task<Guid> SeedStudentConfiguredQuestAsync(AdminDbContext db)
    {
        var policy = await db.ApprovedExperiencePolicies.SingleAsync();
        var quest = new Quest
        {
            SemesterCode = "FA26",
            CampusCode = "GLOBAL",
            Title = "Verified source quest",
            CriteriaVersion = Guid.NewGuid(),
            CriteriaSnapshotJson = JsonSerializer.Serialize(new QuestCriteria("Academic", [1], policy.Id), ExperienceJson)
        };
        db.Quests.Add(quest); await db.SaveChangesAsync(); return quest.Id;
    }
}
