using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ActivityService.Contracts;
using ClubReportHub.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;

namespace Backend.StabilizationTests;
public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    internal static async Task<Guid> PrepareCancellationAsync(Microsoft.AspNetCore.Builder.WebApplication app)
    {
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        var clock=(ManagerClock)app.Services.GetRequiredService<TimeProvider>();var activity=await db.Activities.SingleAsync();activity.StartTimeUtc=clock.Current.AddHours(2);activity.EndTimeUtc=clock.Current.AddHours(3);activity.Capacity=1;
        var session=await db.Sessions.SingleAsync();session.StartTimeUtc=activity.StartTimeUtc;session.EndTimeUtc=activity.EndTimeUtc;session.OpensAt=activity.StartTimeUtc;session.ClosesAt=activity.EndTimeUtc;
        await db.SaveChangesAsync();return (await db.ActivityParticipants.SingleAsync()).Version;
    }
    [Fact]
    public async Task Cancellation_PreservesRecordAndReceipt_ReleasesSeatAndAllowsNewRegistration()
    {
        await using var app=await CreateManagerSessionAppAsync();var version=await PrepareCancellationAsync(app);using var client=CreateAdminContractClient(app,104,AuthRoles.ClubMember);client.DefaultRequestHeaders.Add("Idempotency-Key","cancel");
        var body=new{expectedVersion=version};var response=await client.PostAsJsonAsync("/api/activities/1/registration/cancel",body);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var receipt=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.False(receipt.GetProperty("isRegistered").GetBoolean());Assert.NotEqual(version,receipt.GetProperty("registrationVersion").GetGuid());
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/activities/1/registration/cancel",body)).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ActivityDbContext>();Assert.Empty(await db.ActivityParticipants.ToListAsync());var stored=await db.ActivityParticipants.IgnoreQueryFilters().SingleAsync();Assert.NotNull(stored.CancelledAtUtc);Assert.Equal(0,(await db.Activities.SingleAsync()).RegisteredCount);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");client.DefaultRequestHeaders.Add("Idempotency-Key","register-again");Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/activities/1/participants",new RegisterActivityParticipantRequest(null,null))).StatusCode);
        db.ChangeTracker.Clear();Assert.Single(await db.ActivityParticipants.ToListAsync());Assert.Single(await db.ActivityParticipants.IgnoreQueryFilters().ToListAsync());Assert.Equal(1,(await db.Activities.SingleAsync()).RegisteredCount);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");client.DefaultRequestHeaders.Add("Idempotency-Key","stale-cancel");Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/activities/1/registration/cancel",body)).StatusCode);
    }
    [Fact]
    public async Task Cancellation_IsOwnOnlyAndRejectsAttendanceOrStartedActivity()
    {
        await using var app=await CreateManagerSessionAppAsync();var version=await PrepareCancellationAsync(app);using var other=CreateAdminContractClient(app,105,AuthRoles.ClubMember);other.DefaultRequestHeaders.Add("Idempotency-Key","other");Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsJsonAsync("/api/activities/1/registration/cancel",new{expectedVersion=version})).StatusCode);
        using var client=CreateAdminContractClient(app,104,AuthRoles.ClubMember);client.DefaultRequestHeaders.Add("Idempotency-Key","own");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/activities/1/registration/cancel",new{expectedVersion=version,userId=105})).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ActivityDbContext>();db.ActivityAttendances.Add(new ActivityAttendance{ActivityId=1,UserId=104,FullName="Student A",AttendanceDate=new DateOnly(2026,10,12),Status="Absent"});await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/activities/1/registration/cancel",new{expectedVersion=version})).StatusCode);Assert.Single(await db.ActivityParticipants.ToListAsync());
        db.ActivityAttendances.RemoveRange(await db.ActivityAttendances.ToListAsync());var clock=(ManagerClock)app.Services.GetRequiredService<TimeProvider>();var activity=await db.Activities.SingleAsync();activity.StartTimeUtc=clock.Current;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/activities/1/registration/cancel",new{expectedVersion=version})).StatusCode);
    }
}
public sealed partial class ClubManagerSqlIntegrityTests
{
    [ClubManagerSqlFact]
    public async Task Cancellation_SqlConcurrentCancelAndSeatReuseKeepCounterConsistent()
    {
        await using var database=await ManagerSqlDatabase.CreateAsync();await using var app=await CrossServiceWorkflowAuthorizationTests.CreateManagerSessionAppAsync(database.ConnectionString);var version=await CrossServiceWorkflowAuthorizationTests.PrepareCancellationAsync(app);
        using var first=app.GetTestClient();using var second=app.GetTestClient();first.DefaultRequestHeaders.Authorization=new("Bearer",CrossServiceWorkflowAuthorizationTests.ManagerTestToken(104));second.DefaultRequestHeaders.Authorization=first.DefaultRequestHeaders.Authorization;first.DefaultRequestHeaders.Add("Idempotency-Key","cancel-a");second.DefaultRequestHeaders.Add("Idempotency-Key","cancel-b");
        var responses=await Task.WhenAll(first.PostAsJsonAsync("/api/activities/1/registration/cancel",new{expectedVersion=version}),second.PostAsJsonAsync("/api/activities/1/registration/cancel",new{expectedVersion=version}));Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        first.DefaultRequestHeaders.Remove("Idempotency-Key");first.DefaultRequestHeaders.Add("Idempotency-Key","reg-a");second.DefaultRequestHeaders.Remove("Idempotency-Key");second.DefaultRequestHeaders.Add("Idempotency-Key","reg-b");second.DefaultRequestHeaders.Authorization=new("Bearer",CrossServiceWorkflowAuthorizationTests.ManagerTestToken(105));
        responses=await Task.WhenAll(first.PostAsJsonAsync("/api/activities/1/participants",new RegisterActivityParticipantRequest(null,null)),second.PostAsJsonAsync("/api/activities/1/participants",new RegisterActivityParticipantRequest(null,null)));Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ActivityDbContext>();Assert.Equal(1,await db.ActivityParticipants.CountAsync());Assert.Equal(1,(await db.Activities.SingleAsync()).RegisteredCount);
    }
}
