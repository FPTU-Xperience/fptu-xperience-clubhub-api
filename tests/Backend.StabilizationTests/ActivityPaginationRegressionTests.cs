using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class ActivityActionContractTests
{
    [Fact]
    public async Task Activity_PagedApplicationsAndRecruitmentsKeepBoundsAndDefaultMetadata()
    {
        await using var f = await Fixture.Create();
        var (_, activityId) = await f.Approved();
        using var manager = f.Client(200);
        var staffing = await Json(await manager.GetAsync($"/api/activities/{activityId}/staffing"));
        var actionRuleId = staffing.GetProperty("actions")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/activities/{activityId}/recruitment", new
        {
            title = "Pagination recruitment",
            visibility = "PUBLIC",
            eligibility = "SCHOOL_STUDENTS",
            expectedVersion = 1,
            positions = new[] { new { actionRuleId, capacity = 1, assignmentKind = "STAFF" } }
        }, "pagination-recruitment")).StatusCode);

        foreach (var path in new[] { "/api/activity-applications?clubId=1", "/api/clubs/1/activity-recruitments" })
        {
            var separator = path.Contains('?') ? '&' : '?';
            var first = await Json(await manager.GetAsync(path));
            Assert.Equal(1, first.GetProperty("page").GetInt32());
            Assert.Equal(20, first.GetProperty("pageSize").GetInt32());
            Assert.Equal(1, first.GetProperty("totalItems").GetInt32());
            Assert.Single(first.GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await manager.GetAsync($"{path}{separator}page={int.MaxValue}&pageSize=100")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await manager.GetAsync($"{path}{separator}page=0&pageSize=100")).StatusCode);
            var boundary = await manager.GetAsync($"{path}{separator}page=100000&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
            var empty = await Json(boundary);
            Assert.Equal(100000, empty.GetProperty("page").GetInt32());
            Assert.Equal(100, empty.GetProperty("pageSize").GetInt32());
            Assert.Equal(1, empty.GetProperty("totalItems").GetInt32());
            Assert.Empty(empty.GetProperty("items").EnumerateArray());
        }
    }
}

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Activity_MemberHistoryLargePageReturnsEmptyWithoutLosingAuthoritativeStatistics()
    {
        var builder = ReadEfficiencyBuilder();
        var name = "member-history-page-" + Guid.NewGuid().ToString("N");
        builder.Services.AddDbContext<ActivityDbContext>(options => options.UseInMemoryDatabase(name));
        builder.Services.AddScoped<MemberActivityStatisticsService>();
        builder.Services.AddSingleton(CreateClubAccessClient(CreateAccessSnapshot(1, 100)));
        var now = DateTimeOffset.UtcNow;
        var joined = now.AddDays(-10);
        builder.Services.AddSingleton(new ClubMemberRosterClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new ClubMemberRosterItem(1, 104, "Current member", "member@example.test", "", "Member", "Approved", joined)
            })
        })));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberStatisticsEndpoints();
        int firstActivityId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activities = Enumerable.Range(1, 3).Select(index => new ClubActivity
            {
                ClubId = 1,
                ClubName = "Club 1",
                Title = "Eligible " + index,
                StartTimeUtc = now.AddDays(-index),
                EndTimeUtc = now.AddDays(-index).AddHours(1),
                Status = ActivityStatuses.Completed,
                PublicationStatus = "COMPLETED"
            }).ToArray();
            db.Activities.AddRange(activities);
            db.Activities.Add(new ClubActivity
            {
                ClubId = 1,
                ClubName = "Club 1",
                Title = "Before membership",
                StartTimeUtc = joined.AddDays(-1),
                EndTimeUtc = joined.AddDays(-1).AddHours(1),
                Status = ActivityStatuses.Completed,
                PublicationStatus = "COMPLETED"
            });
            await db.SaveChangesAsync();
            firstActivityId = activities[0].Id;
            db.ActivityAttendances.Add(new ActivityAttendance
            {
                ActivityId = firstActivityId,
                UserId = 104,
                AttendanceDate = DateOnly.FromDateTime(activities[0].StartTimeUtc.UtcDateTime),
                Status = AttendanceStatuses.Present,
                VerificationStatus = "VERIFIED"
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        const string path = "/api/activities/clubs/1/member-statistics/detail";
        var hugeResponse = await manager.PostAsJsonAsync(path,
            new MemberStatisticsDetailQuery(104, joined.AddYears(-5), int.MaxValue, 100));
        Assert.Equal(HttpStatusCode.OK, hugeResponse.StatusCode);
        var huge = await hugeResponse.Content.ReadFromJsonAsync<MemberStatisticsDetailResponse>();
        Assert.NotNull(huge);
        Assert.Equal(int.MaxValue, huge.Page);
        Assert.Equal(100, huge.PageSize);
        Assert.Equal(3, huge.TotalItems);
        Assert.Empty(huge.Items);
        Assert.Equal(3, huge.Statistics.EligibleActivities);
        Assert.Equal(1, huge.Statistics.AttendedActivities);

        var firstResponse = await manager.PostAsJsonAsync(path,
            new MemberStatisticsDetailQuery(104, joined.AddYears(-5), 1, 2));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<MemberStatisticsDetailResponse>();
        Assert.NotNull(first);
        Assert.Equal(huge.Statistics, first.Statistics);
        Assert.Equal(3, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new[] { "Eligible 1", "Eligible 2" }, first.Items.Select(x => x.Title));
        Assert.Equal(firstActivityId, first.Items.First().ActivityId);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync(path,
            new MemberStatisticsDetailQuery(999, joined, int.MaxValue, 100))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(
            "/api/activities/clubs/2/member-statistics/detail",
            new MemberStatisticsDetailQuery(104, joined, int.MaxValue, 100))).StatusCode);
    }
}
