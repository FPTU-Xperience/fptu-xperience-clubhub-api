using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityService.Data;
using ActivityService.Models;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Student_FeedAndCandidatesShareSameCampusPublicAndCurrentMembershipInternalScope()
    {
        await using var app = await CreateStudentActivityAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            foreach (var (title, clubId, internalOnly) in new[] {
                ("Public same", 1, false), ("Public other", 2, false), ("Internal member", 3, true),
                ("Internal cross-campus member", 4, true), ("Internal outsider", 1, true) })
            {
                var row = StudentActivity(title); row.ClubId = clubId; row.IsInternal = internalOnly;
                row.Eligibility = internalOnly ? "CLUB_MEMBER" : "CAMPUS_PUBLIC";
                db.Activities.Add(row);
            }
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var feed = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/feed");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/experience/student/candidates")
        { Content = JsonContent.Create(new StudentCandidateQuery(104, SemesterId: "FA26")) };
        request.Headers.Add("X-Internal-Workflow-Token", TestInternalWorkflowToken);
        var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        var candidates = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var page in new[] { feed, candidates })
        {
            var titles = page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToArray();
            Assert.Equal(3, titles.Length);
            Assert.Contains("Public same", titles);
            Assert.Contains("Internal member", titles);
            Assert.Contains("Internal cross-campus member", titles);
            Assert.DoesNotContain("Public other", titles);
            Assert.DoesNotContain("Internal outsider", titles);
        }
    }

    [Fact]
    public async Task Student_FeedUsesApplicationPublicationAndKeepsLegacyRestrictedToClubReaders()
    {
        await using var app = await CreateStudentActivityAppAsync();
        int legacyId, publishedId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            var legacy = StudentActivity("Authorized legacy");
            legacy.ClubId = 3; legacy.PublicationStatus = "LEGACY"; legacy.DiscoveryVisibility = "CLUB_ONLY"; legacy.IsInternal = true;
            legacy.StartTimeUtc = DateTimeOffset.UtcNow.AddDays(-40);
            legacy.EndTimeUtc = DateTimeOffset.UtcNow.AddDays(40);
            var outsiderLegacy = StudentActivity("Not a member legacy"); outsiderLegacy.PublicationStatus = "LEGACY"; outsiderLegacy.IsInternal = true;
            var expired = StudentActivity("Expired legacy"); expired.ClubId = 3; expired.PublicationStatus = "LEGACY";
            expired.StartTimeUtc = DateTimeOffset.UtcNow.AddDays(-2); expired.EndTimeUtc = DateTimeOffset.UtcNow.AddDays(-1);
            var published = StudentActivity("Published execution"); published.PublicationStatus = "DRAFT";
            var unpublished = StudentActivity("Unpublished execution");
            db.AddRange(legacy, outsiderLegacy, expired, published, unpublished);
            await db.SaveChangesAsync(); legacyId = legacy.Id; publishedId = published.Id;
            db.AddRange(new ActivityExecution { ActivityId = published.Id, ApplicationId = 901, IsPublished = true },
                new ActivityExecution { ActivityId = unpublished.Id, ApplicationId = 902, IsPublished = false });
            await db.SaveChangesAsync();
        }
        using var client = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var feed = await client.GetFromJsonAsync<JsonElement>("/api/v2/activities/feed");
        var items = feed.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("LIVE", items.Single(x => x.GetProperty("id").GetInt32() == legacyId).GetProperty("status").GetString());
        Assert.Contains(items, x => x.GetProperty("id").GetInt32() == publishedId);
        Assert.DoesNotContain("Not a member", feed.GetRawText());
        Assert.DoesNotContain("Unpublished execution", feed.GetRawText());
        Assert.DoesNotContain("Expired legacy", feed.GetRawText());
        Assert.DoesNotContain("participants", feed.GetRawText());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v2/activities/{legacyId}")).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/experience/student/candidates")
        { Content = JsonContent.Create(new StudentCandidateQuery(104, 1, 100, "FA26")) };
        request.Headers.Add("X-Internal-Workflow-Token", TestInternalWorkflowToken);
        var candidates = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
        var candidatePage = await candidates.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain("Authorized legacy", candidatePage.GetRawText());
    }
}
