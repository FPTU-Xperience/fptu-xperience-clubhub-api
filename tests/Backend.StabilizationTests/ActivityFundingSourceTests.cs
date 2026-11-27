using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Models;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

public sealed partial class ActivityActionContractTests
{
    [Fact]
    public async Task FundingSourceRequiresInternalAuthorityAndBindsCurrentExecutionCandidatesAndCancellation()
    {
        await using var f = await Fixture.Create(); var (applicationId, activityId) = await f.Approved();
        using var client = f.Client(202);
        var path = $"/api/internal/experience/funding-sources/APPLICATION:{applicationId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        var reservationId = Guid.NewGuid();
        await f.WithDb(async db =>
        {
            var application = await db.Set<ActivityApplication>().SingleAsync(x => x.Id == applicationId);
            var plan = JsonSerializer.Deserialize<ActivityApplicationInput>(application.ContentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            application.ContentJson = JsonSerializer.Serialize(plan with
            {
                RawBudget = 60,
                PolicyVersion = CampusPolicyVersion,
                FundingAttributes = new("T2", "Participant", "Club", "None", [new("Academic", 1)])
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var activity = await db.Activities.SingleAsync(x => x.Id == activityId);
            activity.FundingReservationId = reservationId; activity.RawBudget = 60; activity.PolicyVersion = CampusPolicyVersion;
            db.Add(new ActivityFundingCandidate { ActivityId = activityId, UserId = 100, SourceKey = $"CLUB:{activityId}:SESSION:1" });
            db.Add(new ActivityClosingReport { ActivityId = activityId, Version = Guid.NewGuid(), Status = "APPROVED", SubmittedBy = 200, SubmittedAtUtc = Instant, ReviewedBy = 202, ReviewedAtUtc = Instant });
            await db.SaveChangesAsync();
        });
        client.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", "test-internal-token");
        var source = await client.GetFromJsonAsync<RawFundingSource>(path);
        Assert.NotNull(source); Assert.Equal(applicationId.ToString(), source.SourceKey.Split(':')[1]);
        Assert.Equal(reservationId, source.FundingReservationId); Assert.Equal(activityId, source.ActivityId);
        Assert.Equal(60, source.Budget); Assert.Equal(CampusPolicyVersion, source.PolicyVersion);
        Assert.True(source.ClosingApproved); Assert.Equal(100, Assert.Single(source.Candidates).UserId);
        var version = source.Version;
        await f.WithDb(async db =>
        {
            var application = await db.Set<ActivityApplication>().SingleAsync(x => x.Id == applicationId);
            application.Version++; await db.SaveChangesAsync();
        });
        Assert.NotEqual(version, (await client.GetFromJsonAsync<RawFundingSource>(path))!.Version);
        await f.WithDb(async db => { (await db.Activities.SingleAsync(x => x.Id == activityId)).Status = ActivityStatuses.Cancelled; await db.SaveChangesAsync(); });
        source = await client.GetFromJsonAsync<RawFundingSource>(path);
        Assert.Equal("CANCELLED", source!.Status); Assert.False(source.ClosingApproved);
        Assert.Equal(reservationId, source.FundingReservationId);
    }
}
