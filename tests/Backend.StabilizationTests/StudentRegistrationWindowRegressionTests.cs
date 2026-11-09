using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ClubReportHub.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Theory]
    [InlineData(null, null, false, "Scheduled", true)]
    [InlineData(null, 60, false, "Scheduled", true)]
    [InlineData(-60, null, false, "Scheduled", true)]
    [InlineData(60, null, false, "Scheduled", false)]
    [InlineData(null, -60, false, "Scheduled", false)]
    [InlineData(-60, 60, false, "Scheduled", true)]
    [InlineData(null, null, true, "Scheduled", false)]
    [InlineData(null, null, false, "Cancelled", false)]
    public async Task Student_RegistrationWindowMatchesWriteContract(int? opensMinutes, int? closesMinutes, bool full,
        string activityStatus, bool expected)
    {
        await using var app = await CreateStudentActivityAppAsync();
        int id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Nullable registration windows");
            var now = DateTimeOffset.UtcNow;
            activity.RegistrationOpensAt = opensMinutes.HasValue ? now.AddMinutes(opensMinutes.Value) : null;
            activity.RegistrationClosesAt = closesMinutes.HasValue ? now.AddMinutes(closesMinutes.Value) : null;
            activity.Status = activityStatus;
            activity.Capacity = full ? 1 : null;
            activity.RegisteredCount = full ? 1 : 0;
            db.Add(activity);
            await db.SaveChangesAsync();
            id = activity.Id;
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v2/activities/{id}");
        Assert.True(detail.GetProperty("isEligible").GetBoolean());
        Assert.Equal(expected, detail.GetProperty("canRegister").GetBoolean());
        Assert.Equal(expected ? "REGISTER" : "VIEW", detail.GetProperty("nextAction").GetString());
    }
}
