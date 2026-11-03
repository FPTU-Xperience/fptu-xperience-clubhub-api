using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Student_PublicRegistrationAndCandidatesIgnoreStaleMemberEligibility()
    {
        await using var app = await CreateStudentActivityAppAsync();
        int id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Public with stale eligibility");
            activity.IsInternal = false;
            activity.Eligibility = "CLUB_MEMBER";
            db.Add(activity);
            await db.SaveChangesAsync();
            id = activity.Id;
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v2/activities/{id}");
        Assert.True(detail.GetProperty("canRegister").GetBoolean());
        Assert.True(detail.GetProperty("isEligible").GetBoolean());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/experience/student/candidates")
        { Content = JsonContent.Create(new StudentCandidateQuery(104, SemesterId: "FA26")) };
        request.Headers.Add("X-Internal-Workflow-Token", TestInternalWorkflowToken);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var candidates = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(candidates.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Student_InternalActivityNeverUsesStalePublicEligibilityToAdmitOutsiders()
    {
        await using var app = await CreateStudentActivityAppAsync();
        int id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var activity = StudentActivity("Internal with stale eligibility");
            activity.IsInternal = true;
            activity.Eligibility = "CAMPUS_PUBLIC";
            db.Add(activity);
            await db.SaveChangesAsync();
            id = activity.Id;
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v2/activities/{id}")).StatusCode);
    }
}

public sealed class ActivityAudienceRegressionTests
{
    [Theory]
    [InlineData(false, "CLUB_MEMBER", false, "HN", true)]
    [InlineData(false, "CAMPUS_PUBLIC", false, "HN", true)]
    [InlineData(false, "CLUB_MEMBER", false, "HCM", false)]
    [InlineData(true, "CAMPUS_PUBLIC", false, "HN", false)]
    [InlineData(true, "CAMPUS_PUBLIC", true, "HN", true)]
    [InlineData(true, "CLUB_MEMBER", true, "HCM", true)]
    public async Task RegistrationUsesIsInternalAndRetainsCampusAndApprovedMembership(
        bool isInternal, string legacyEligibility, bool member, string campus, bool allowed)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ActivityDbContext>(o => o.UseSqlite(connection));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Security:InternalWorkflowToken"] = "test-audience-token-only" }).Build();
        var users = new CurrentUserProfileClient(new HttpClient(new AudienceHandler(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new[] { new InternalUserProfile(104, "Trusted student", "test@example.invalid", campus, true, false, null) }) }))
        { BaseAddress = new Uri("http://test.invalid/") }, config);
        var clubs = new ClubExperienceAuthorityClient(new HttpClient(new AudienceHandler(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new ClubExperienceAuthority(1, "Club", "HN", true, member, member ? DateTimeOffset.UtcNow.AddYears(-1) : null)) }))
        { BaseAddress = new Uri("http://test.invalid/") }, config);
        services.AddSingleton(users);
        services.AddSingleton(clubs);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        await db.Database.EnsureCreatedAsync();
        var activity = new ClubActivity
        {
            ClubId = 1, ClubName = "Club", Title = "Audience regression", Description = "Test", Location = "Hall",
            IsInternal = isInternal, Eligibility = legacyEligibility, PublicationStatus = "PUBLISHED",
            StartTimeUtc = DateTimeOffset.UtcNow.AddDays(2), EndTimeUtc = DateTimeOffset.UtcNow.AddDays(2).AddHours(1),
            RegistrationOpensAt = DateTimeOffset.UtcNow.AddDays(-1), RegistrationClosesAt = DateTimeOffset.UtcNow.AddDays(1),
            Capacity = 10
        };
        db.Add(activity);
        await db.SaveChangesAsync();
        Assert.Equal(allowed, await ActivityEligibility.ResolveAsync(activity, 104, activity.StartTimeUtc, users, clubs, default) is not null);
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "104") }, "test")) };
        http.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        var roster = new ClubMemberRosterClient(new HttpClient(new AudienceHandler(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(member ? new[] { new { id = 1, clubId = 1, userId = 104, fullName = "Trusted student", status = "Approved", requestedAtUtc = DateTimeOffset.UtcNow.AddYears(-1) } } : []) }))
        { BaseAddress = new Uri("http://test.invalid/") });
        var type = typeof(StudentActivityEndpoints).Assembly.GetType("ActivityService.Endpoints.ActivityWriteEndpoints")!;
        var handler = type.GetMethod("RegisterParticipant", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<IResult>)handler.Invoke(null, new object?[] { activity.Id,
            new RegisterActivityParticipantRequest(null, null), db, http.User, http, null, roster, CancellationToken.None })!;
        var result = await task;
        if (allowed) Assert.Equal(200, ((IStatusCodeHttpResult)result).StatusCode);
        else Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
        Assert.Equal(allowed ? 1 : 0, await db.ActivityParticipants.CountAsync());
    }

    private sealed class AudienceHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }
}
