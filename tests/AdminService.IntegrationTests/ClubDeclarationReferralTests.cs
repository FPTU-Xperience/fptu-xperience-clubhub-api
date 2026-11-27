using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminService.IntegrationTests;

public sealed class ClubDeclarationReferralTests
{
    [Theory]
    [InlineData("Academic")]
    [InlineData("RealWorldWork")]
    public async Task ReferralRequiresIndependentClubReviewAndForwardBeforeCampusDecision(string pillar)
    {
        await using var f = new ReferralFactory();
        await f.EnsureStudentPolicyAsync();
        using var student = Client(f, "student-1");
        using var bod = Client(f, "admin");
        using var staff = Client(f, "student-affairs-han");
        using var foreign = Client(f, "student-affairs-hcm");
        var submitted = await Submit(f, student, pillar);
        var id = submitted.GetProperty("id").GetGuid();
        var evidence = submitted.GetProperty("evidenceRefs")[0].GetGuid();
        Assert.Equal("PENDING", submitted.GetProperty("clubReviewStatus").GetString());
        Assert.Equal(1, submitted.GetProperty("referringClubId").GetInt32());
        await MissingFromQueue(staff, id);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/declarations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/contributions/evidence/{evidence}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bod.GetAsync($"/api/contributions/evidence/{evidence}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decision(staff, id, submitted, "APPROVE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClubAction(student, id, submitted, "club-review", "ENDORSE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClubAction(foreign, id, submitted, "club-review", "ENDORSE")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ClubAction(bod, id, submitted, "forward")).StatusCode);
        var endorsed = await Json(await ClubAction(bod, id, submitted, "club-review", "ENDORSE"));
        await MissingFromQueue(staff, id);
        Assert.Equal(HttpStatusCode.Conflict, (await ClubAction(bod, id, submitted, "forward")).StatusCode);
        var forwarded = await Json(await ClubAction(bod, id, endorsed, "forward"));
        var queue = await staff.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/declarations?viaClub=true&pageSize=100");
        Assert.Contains(queue.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/contributions/evidence/{evidence}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decision(bod, id, forwarded, "APPROVE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decision(foreign, id, forwarded, "APPROVE")).StatusCode);
        await NoXp(f, id);
        var review = await Decision(staff, id, forwarded, "APPROVE");
        var approved = await Json(review);
        Assert.Equal("APPROVED", approved.GetProperty("status").GetString());
        Assert.True(approved.GetProperty("rawPoints").GetDecimal() > 0);
        // Same request key replays; a different request key with the old version cannot award twice.
        var replay = await Decision(staff, id, forwarded, "APPROVE", preserveKey: true);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Decision(staff, id, forwarded, "APPROVE")).StatusCode);
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        Assert.Single(await db.ExperienceLedgerEffects.Where(x => x.ContributionId == id).ToListAsync());
        Assert.Equal("Approved", (await db.SelfDeclarations.SingleAsync(x => x.Id == id)).Status);
        Assert.Equal(1, await db.ContributionDecisions.CountAsync(x => x.ContributionId == id));
    }

    [Fact]
    public async Task AuthorRevisionsResetBothReferralStagesAndRevokedAuthorityCannotForward()
    {
        await using var f = new ReferralFactory(); await f.EnsureStudentPolicyAsync();
        using var student = Client(f, "student-1"); using var bod = Client(f, "admin"); using var staff = Client(f, "student-affairs-han");
        var row = await Submit(f, student, "Academic"); var id = row.GetProperty("id").GetGuid();
        var changes = await Json(await ClubAction(bod, id, row, "club-review", "REQUEST_CHANGES"));
        Assert.Equal("REVISION_REQUESTED", changes.GetProperty("status").GetString());
        var revised = await Revise(student, id, changes);
        Assert.Equal("PENDING", revised.GetProperty("clubReviewStatus").GetString());
        var endorsed = await Json(await ClubAction(bod, id, revised, "club-review", "ENDORSE"));
        f.OwnerLocked = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await ClubAction(bod, id, endorsed, "forward")).StatusCode);
        f.OwnerLocked = false;
        f.Revoked = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await ClubAction(bod, id, endorsed, "forward")).StatusCode);
        var evidence = endorsed.GetProperty("evidenceRefs")[0].GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await bod.GetAsync($"/api/contributions/evidence/{evidence}")).StatusCode);
        f.Revoked = false;
        var forwarded = await Json(await ClubAction(bod, id, endorsed, "forward"));
        var returned = await Json(await Decision(staff, id, forwarded, "REQUEST_REVISION"));
        var second = await Revise(student, id, returned);
        Assert.Equal("PENDING", second.GetProperty("clubReviewStatus").GetString());
        Assert.Equal("NOT_SUBMITTED", second.GetProperty("ctsvSubmissionStatus").GetString());
        await MissingFromQueue(staff, id);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decision(staff, id, second, "APPROVE")).StatusCode);
        var again = await Json(await ClubAction(bod, id, second, "club-review", "ENDORSE"));
        var sent = await Json(await ClubAction(bod, id, again, "forward"));
        Assert.Equal("REJECTED", (await Json(await Decision(staff, id, sent, "REJECT"))).GetProperty("status").GetString());
        await NoXp(f, id);
    }

    [Fact]
    public async Task ClubListFiltersBeforePagingAndNonMembersCannotChooseAReferringClub()
    {
        await using var f = new ReferralFactory(); await f.EnsureStudentPolicyAsync();
        using var student = Client(f, "student-1"); using var bod = Client(f, "admin");
        var first = await Submit(f, student, "Academic");
        var wrong = await Submit(f, student, "Academic", clubId: 2, expected: HttpStatusCode.Forbidden);
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            for (var i = 0; i < 24; i++) db.Contributions.Add(new VerifiedContribution
            {
                UserId = 302,
                ClubId = 2,
                ClubReviewStatus = "PENDING",
                CtsvSubmissionStatus = "NOT_SUBMITTED",
                SourceType = "EXTERNAL",
                SourceKey = Guid.NewGuid().ToString(),
                Title = "Other club",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ProposedAttributesJson = first.GetProperty("proposedAttributes").GetRawText(),
                SourceSnapshotJson = "{}"
            });
            await db.SaveChangesAsync();
        }
        var page = await bod.GetFromJsonAsync<JsonElement>("/api/v1/declarations/clubs/1?page=1&pageSize=1");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(first.GetProperty("id").GetGuid(), Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var empty = await bod.GetFromJsonAsync<JsonElement>("/api/v1/declarations/clubs/1?page=2&pageSize=1");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await bod.GetAsync("/api/v1/declarations/clubs/2")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bod.GetAsync("/api/v1/declarations/clubs/1?page=0")).StatusCode);
    }

    [Fact]
    public async Task AssignedVerifierCannotBypassReferralStagesOrCampusThroughAlternativeRoutes()
    {
        await using var f = new ReferralFactory(); await f.EnsureStudentPolicyAsync();
        using var student = Client(f, "student-1"); using var foreign = Client(f, "student-affairs-hcm");
        var row = await Submit(f, student, "Academic"); var id = row.GetProperty("id").GetGuid();
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.VerificationAssignments.Add(new VerificationAssignment
            {
                UserId = 203,
                ClubId = 1,
                SourceType = "EXTERNAL",
                CapabilitiesJson = "[\"contribution.review\",\"experience.adjust\"]",
                StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
                Reason = "Fixture assigned grant"
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync($"/api/contributions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync($"/api/contributions/evidence/{row.GetProperty("evidenceRefs")[0].GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decision(foreign, id, row, "APPROVE")).StatusCode);
        Key(foreign);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsJsonAsync($"/api/contributions/{id}/role-upgrade-requests/{Guid.NewGuid()}/reviews",
            new { decision = "APPROVE", expectedVersion = row.GetProperty("version").GetGuid(), reason = "Forbidden assigned bypass" })).StatusCode);
        Key(foreign);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsJsonAsync($"/api/contributions/{id}/adjustments",
            new { kind = "CORRECT", expectedVersion = row.GetProperty("version").GetGuid(), reason = "Forbidden correction bypass", correctedAttributes = row.GetProperty("proposedAttributes") })).StatusCode);
        await NoXp(f, id);
        var queue = await foreign.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/declarations?pageSize=100");
        Assert.Empty(queue.GetProperty("items").EnumerateArray());
    }

    private static HttpClient Client(ReferralFactory f, string token)
    { var client = f.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client; }
    private static void Key(HttpClient client) { client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString()); }
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task MissingFromQueue(HttpClient staff, Guid id)
    { var queue = await staff.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/declarations?pageSize=100"); Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id); }
    private static async Task NoXp(ReferralFactory f, Guid id)
    { await using var scope = f.Services.CreateAsyncScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<AdminDbContext>().ExperienceLedgerEffects.AnyAsync(x => x.ContributionId == id)); }
    private static async Task<JsonElement> Submit(ReferralFactory f, HttpClient client, string pillar, int clubId = 1, HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var upload = new MultipartFormDataContent(); var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfO0AAAAASUVORK5CYII="));
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png"); upload.Add(png, "file", "referral.png"); Key(client);
        var file = await Json(await client.PostAsync("/api/contributions/evidence", upload));
        Key(client);
        var response = await client.PostAsJsonAsync("/api/v1/declarations", new SubmitDeclarationRequest("Actual referral", pillar, "External organization", true, null, "", "Evidence details", "Participant", new(2026, 11, 18), new(2026, 11, 18),
            SemesterId: "FA26", OccurredAt: new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero), ProposedAttributes: new("T1", "Participant", "National", "None", [new(pillar, 1)]), EvidenceRefs: [file.GetProperty("evidenceId").GetGuid()], PolicyVersion: f.StudentPolicyVersion, ReferringClubId: clubId));
        Assert.Equal(expected, response.StatusCode);
        return expected == HttpStatusCode.Created ? await response.Content.ReadFromJsonAsync<JsonElement>() : default;
    }
    private static async Task<HttpResponseMessage> ClubAction(HttpClient client, Guid id, JsonElement row, string path, string? decision = null)
    {
        Key(client); object payload = decision is null ? new { expectedVersion = row.GetProperty("version").GetGuid(), reason = "Club reviewed the supplied evidence." }
            : new { expectedVersion = row.GetProperty("version").GetGuid(), reason = "Club reviewed the supplied evidence.", decision };
        return await client.PostAsJsonAsync($"/api/v1/declarations/clubs/1/{id}/{path}", payload);
    }
    private static Task<HttpResponseMessage> Decision(HttpClient client, Guid id, JsonElement row, string decision, bool preserveKey = false)
    { if (!preserveKey) Key(client); return client.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review", new { expectedVersion = row.GetProperty("version").GetGuid(), decision, reason = "Independent campus evidence verification.", verifiedAttributes = row.GetProperty("proposedAttributes") }); }
    private static async Task<JsonElement> Revise(HttpClient client, Guid id, JsonElement row)
    { Key(client); return await Json(await client.PatchAsJsonAsync($"/api/contributions/{id}", new { expectedVersion = row.GetProperty("version").GetGuid(), proposedAttributes = row.GetProperty("proposedAttributes"), message = "Updated supporting evidence.", evidenceRefs = row.GetProperty("evidenceRefs") })); }

    private sealed class ReferralFactory : AdminApiFactory
    {
        internal bool Revoked;
        internal bool OwnerLocked;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<CurrentUserProfileClient>(provider => new(new HttpClient(new Authority(this)) { BaseAddress = new Uri("http://referral-fixture.invalid/") }, provider.GetRequiredService<IConfiguration>()));
                services.AddSingleton<ClubExperienceAuthorityClient>(provider => new(new HttpClient(new Authority(this)) { BaseAddress = new Uri("http://referral-fixture.invalid/") }, provider.GetRequiredService<IConfiguration>()));
                services.AddHttpClient("ExperienceClub").ConfigurePrimaryHttpMessageHandler(() => new Authority(this));
            });
        }
    }
    private sealed class Authority(ReferralFactory f) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object value;
            if (request.RequestUri!.AbsolutePath.EndsWith("/user-profiles"))
            {
                var ids = await request.Content!.ReadFromJsonAsync<int[]>(ct) ?? [];
                value = ids.Select(id => new InternalUserProfile(id, "Test account " + id, id + "@example.invalid", id is 203 or 302 ? "HCM" : "HAN", true, f.OwnerLocked && id == 101, null)).ToArray();
            }
            else if (request.RequestUri.AbsolutePath == "/api/clubs/me/access")
                value = request.Headers.Authorization?.Parameter == "admin" && !f.Revoked
                    ? new[] { new ClubAccessSnapshot(1, "Test Club", true, false, true, [101]) } : [];
            else
            {
                var clubId = request.RequestUri.AbsolutePath.EndsWith("/1") ? 1 : 2;
                value = new ClubExperienceAuthority(clubId, "Test Club", clubId == 1 ? "HAN" : "HCM", true, clubId == 1, DateTimeOffset.UtcNow.AddYears(-1));
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        }
    }
}
