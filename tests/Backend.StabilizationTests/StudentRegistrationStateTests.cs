using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Theory]
    [InlineData("PUBLISHED")]
    [InlineData("LEGACY")]
    public async Task Student_RegistrationStateIsPersistedAndScopedToCurrentUser(string publication)
    {
        await using var app = await CreateStudentActivityAppAsync();
        int id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Persisted registration");
            activity.PublicationStatus = publication;
            db.Add(activity);
            await db.SaveChangesAsync();
            id = activity.Id;
            db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = id, UserId = 105, FullName = "Other student" });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/v2/activities/{id}");
        Assert.False(before.GetProperty("isRegistered").GetBoolean());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = id, UserId = 104, FullName = "Current student" });
            await db.SaveChangesAsync();
        }
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v2/activities/{id}");
        Assert.True(detail.GetProperty("isRegistered").GetBoolean());
        Assert.False(detail.GetProperty("canRegister").GetBoolean());
        var feed = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/feed");
        Assert.True(feed.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("id").GetInt32() == id)
            .GetProperty("isRegistered").GetBoolean());
    }
}
