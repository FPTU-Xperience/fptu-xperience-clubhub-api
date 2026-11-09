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
    [InlineData(false, "Scheduled", false, true)]
    [InlineData(true, "Scheduled", false, true)]
    [InlineData(true, "Scheduled", true, false)]
    [InlineData(true, "Completed", false, false)]
    [InlineData(true, "Cancelled", false, false)]
    public async Task Student_LegacyRegistrationCapabilityMatchesExistingWriter(bool internalOnly, string status,
        bool registered, bool expected)
    {
        await using var app = await CreateStudentActivityAppAsync();
        int id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Legacy registration");
            activity.ClubId = internalOnly ? 3 : 1;
            activity.IsInternal = internalOnly;
            activity.PublicationStatus = "LEGACY";
            activity.Status = status;
            activity.RegistrationOpensAt = null;
            activity.RegistrationClosesAt = null;
            db.Add(activity);
            await db.SaveChangesAsync();
            id = activity.Id;
            if (registered)
            {
                db.ActivityParticipants.Add(new ActivityParticipant { ActivityId = id, UserId = 104, FullName = "Student" });
                await db.SaveChangesAsync();
            }
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v2/activities/{id}");
        Assert.True(detail.GetProperty("isEligible").GetBoolean());
        Assert.Equal(expected, detail.GetProperty("canRegister").GetBoolean());
        Assert.Equal(expected ? "REGISTER" : "VIEW", detail.GetProperty("nextAction").GetString());
    }
}
