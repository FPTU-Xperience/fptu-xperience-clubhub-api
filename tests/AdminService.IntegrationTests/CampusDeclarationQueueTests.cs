using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdminService.IntegrationTests;

public sealed class CampusDeclarationQueueTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    [Theory]
    [InlineData("HAN", "student-affairs-han", "student-affairs-hcm", "EXTERNAL", "Academic")]
    [InlineData("HCM", "student-affairs-hcm", "student-affairs-dan", "EXTERNAL", "Academic")]
    [InlineData("DAN", "student-affairs-dan", "student-affairs-han", "EXTERNAL", "Academic")]
    [InlineData("HAN", "student-affairs-han", "student-affairs-hcm", "REAL_WORLD", "RealWorldWork")]
    [InlineData("HCM", "student-affairs-hcm", "student-affairs-dan", "REAL_WORLD", "RealWorldWork")]
    [InlineData("DAN", "student-affairs-dan", "student-affairs-han", "REAL_WORLD", "RealWorldWork")]
    public async Task DirectDeclaration_VisibleAndReviewableOnlyByCampusStaff(
        string campus, string staffToken, string foreignToken, string sourceType, string pillar)
    {
        var id = Guid.NewGuid();
        var evidence = Guid.NewGuid();
        var attributes = new ContributionAttributes("T1", "Participant", "Club", "None", [new(pillar, 1)]);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.SelfDeclarations.Add(new SelfDeclaration
            {
                Id = id,
                StudentId = 301,
                CampusCode = campus,
                IsOutsideClub = true,
                Title = $"QA queue {id}",
                Category = pillar,
                Status = "Submitted"
            });
            db.Contributions.Add(new VerifiedContribution
            {
                Id = id,
                UserId = 301,
                SourceType = sourceType,
                SourceKey = $"DECLARATION:{id:N}",
                Title = $"QA queue {id}",
                SemesterId = "FALL2026",
                Status = "SUBMITTED",
                ProposedAttributesJson = JsonSerializer.Serialize(attributes, json),
                EvidenceRefsJson = JsonSerializer.Serialize(new[] { evidence }),
                SourceSnapshotJson = "{}"
            });
            db.ContributionEvidence.Add(new PrivateContributionEvidence
            {
                Id = evidence,
                OwnerUserId = 301,
                Name = "qa.png",
                ContentType = "image/png",
                Bytes = [1, 2, 3],
                SizeBytes = 3
            });
            await db.SaveChangesAsync();
        }
        using var staff = Client(staffToken);
        staff.DefaultRequestHeaders.Add("Idempotency-Key", $"qa-declaration-{id}");
        using var foreign = Client(foreignToken);
        using var student = Client("student-2");
        foreach (var status in new[] { "Submitted", "" })
        {
            var path = $"/api/v1/student-affairs/declarations?pageSize=100&status={status}";
            var page = await staff.GetFromJsonAsync<JsonElement>(path);
            Assert.Contains(page.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
            Assert.True(page.GetProperty("pagination").GetProperty("totalItems").GetInt32() > 0);
            var otherPage = await foreign.GetFromJsonAsync<JsonElement>(path);
            Assert.DoesNotContain(otherPage.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        }
        var detail = await staff.GetFromJsonAsync<JsonElement>($"/api/v1/declarations/{id}");
        var version = detail.GetProperty("contribution").GetProperty("version").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/contributions/evidence/{evidence}")).StatusCode);
        foreach (var denied in new[] { foreign, student })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync($"/api/v1/declarations/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync($"/api/contributions/evidence/{evidence}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review",
                new { decision = "REQUEST_REVISION", reason = "QA clarification", expectedVersion = version })).StatusCode);
        }
        var reviewed = await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review",
            new { decision = "REQUEST_REVISION", reason = "QA clarification", expectedVersion = version });
        Assert.True(reviewed.IsSuccessStatusCode, await reviewed.Content.ReadAsStringAsync());
        await using var verify = factory.Services.CreateAsyncScope();
        var persisted = verify.ServiceProvider.GetRequiredService<AdminDbContext>();
        Assert.Equal("RevisionRequested", (await persisted.SelfDeclarations.SingleAsync(x => x.Id == id)).Status);
        Assert.Equal("REVISION_REQUESTED", (await persisted.Contributions.SingleAsync(x => x.Id == id)).Status);
        Assert.Null((await persisted.ContributionDecisions.SingleAsync(x => x.ContributionId == id)).AssignmentId);
        Assert.False(await persisted.ExperienceLedgerEffects.AnyAsync(x => x.ContributionId == id));
    }

    [Fact]
    public async Task StaffRole_DoesNotAllowSelfReviewOrUnlinkedContributionAccess()
    {
        var own = Guid.NewGuid();
        var unlinked = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            var attributes = JsonSerializer.Serialize(new ContributionAttributes("T1", "Participant", "Club", "None", [new("Academic", 1)]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            db.SelfDeclarations.Add(new SelfDeclaration { Id = own, StudentId = 202, CampusCode = "HAN", IsOutsideClub = true, Title = "Staff own QA", Category = "Academic" });
            db.Contributions.AddRange(new[] { own, unlinked }.Select(id => new VerifiedContribution
            {
                Id = id,
                UserId = id == own ? 202 : 301,
                SourceType = "EXTERNAL",
                SourceKey = $"DECLARATION:{id:N}",
                Title = "QA scoped rights",
                ProposedAttributesJson = attributes,
                SourceSnapshotJson = "{}"
            }));
            await db.SaveChangesAsync();
        }
        using var staff = Client("student-affairs-han");
        var queue = await staff.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/declarations?pageSize=100");
        Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == own);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/contributions/{unlinked}")).StatusCode);
        foreach (var id in new[] { own, unlinked })
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review",
                new { decision = "REJECT", reason = "QA boundary", expectedVersion = Guid.NewGuid() })).StatusCode);
    }

    [Theory]
    [InlineData("HAN", "student-affairs-han", "student-affairs-hcm")]
    [InlineData("HCM", "student-affairs-hcm", "student-affairs-dan")]
    [InlineData("DAN", "student-affairs-dan", "student-affairs-han")]
    public async Task LegacyDeclaration_DirectEndpointsEnforceTheQueueCampusBoundary(
        string campus, string staffToken, string foreignToken)
    {
        var id = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.SelfDeclarations.Add(new SelfDeclaration
            {
                Id = id,
                StudentId = 301,
                CampusCode = campus,
                IsOutsideClub = true,
                Title = $"Legacy campus boundary {id}",
                Category = "Academic",
                Status = "Submitted"
            });
            await db.SaveChangesAsync();
        }
        using var foreign = Client(foreignToken);
        var queue = await foreign.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/declarations?pageSize=100");
        Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync($"/api/v1/declarations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review",
            new { decision = "RevisionRequested", reviewNote = "Foreign campus must not change this record." })).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal("Submitted", (await scope.ServiceProvider.GetRequiredService<AdminDbContext>().SelfDeclarations.SingleAsync(x => x.Id == id)).Status);
        using var owner = Client("student-1");
        using var admin = Client("admin");
        using var staff = Client(staffToken);
        foreach (var allowed in new[] { owner, admin, staff })
            Assert.Equal(HttpStatusCode.OK, (await allowed.GetAsync($"/api/v1/declarations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review",
            new { decision = "RevisionRequested", reviewNote = "Please clarify the supplied evidence." })).StatusCode);
    }

    [Fact]
    public async Task LegacySubmission_TakesCampusFromTheAuthenticatedActor()
    {
        using var student = Client("student-2");
        var response = await student.PostAsJsonAsync("/api/v1/declarations", new
        {
            title = "Legacy forged campus",
            category = "Academic",
            organizationSource = "External course",
            isOutsideClub = true,
            evidenceUrl = "https://example.invalid/evidence",
            evidenceDescription = "Course certificate",
            startDate = "2026-09-01",
            endDate = "2026-09-02",
            campusCode = "HAN"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("HCM", saved.GetProperty("campusCode").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal("HCM", (await scope.ServiceProvider.GetRequiredService<AdminDbContext>().SelfDeclarations
            .SingleAsync(x => x.Id == saved.GetProperty("id").GetGuid())).CampusCode);
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
