using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using ClubReportHub.Shared.Experience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdminService.IntegrationTests;

// Canonical submit/revise/reject/approve and exact XP effects are covered by
// CampusDeclarationLifecycleTests. These protect the retired intake boundary and
// the explicit, history-preserving transition for existing pending declarations.
public sealed class SelfDeclarationTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    [Fact]
    public async Task LegacyIntakeCannotCreateUnversionedRecordsAndStillRequiresAuthentication()
    {
        using var anonymous = factory.CreateClient(); using var student = Client("student-1");
        var input = Input();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/declarations", input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync("/api/v1/declarations", input)).StatusCode);
        await Db(async db => Assert.False(await db.SelfDeclarations.AnyAsync(x => x.Title == input.Title)));
    }

    [Theory]
    [InlineData("student-1", HttpStatusCode.Forbidden)]
    [InlineData("student-affairs", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    public async Task ReviewQueueRetainsItsStaffBoundary(string token, HttpStatusCode expected)
    {
        using var client = Client(token);
        Assert.Equal(expected, (await client.GetAsync("/api/v1/student-affairs/declarations")).StatusCode);
    }

    [Fact]
    public async Task HistoricalDetailIsOwnedAndLegacyUpdateCannotRewriteIt()
    {
        var legacy = await Seed("Submitted"); using var student = Client("student-1"); using var other = Client("student-2");
        var detail = await student.GetFromJsonAsync<JsonElement>($"/api/v1/declarations/{legacy.Id}");
        Assert.False(detail.GetProperty("isAuthoritative").GetBoolean()); Assert.True(detail.GetProperty("canUpgradeLegacy").GetBoolean());
        Assert.Equal(64, detail.GetProperty("legacyVersion").GetString()!.Length);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/v1/declarations/{legacy.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync($"/api/v1/declarations/{legacy.Id}", Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PutAsJsonAsync($"/api/v1/declarations/{legacy.Id}", Input())).StatusCode);
        await Db(async db => Assert.Equal(legacy.Title, (await db.SelfDeclarations.SingleAsync(x => x.Id == legacy.Id)).Title));
    }

    [Theory]
    [InlineData("Submitted")]
    [InlineData("RevisionRequested")]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task LegacyReviewCannotWriteUnledgeredDecisionsOrRecalculateHistory(string state)
    {
        var legacy = await Seed(state); using var staff = Client("student-affairs-han");
        var response = await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{legacy.Id}/review", new ReviewDeclarationRequest("APPROVED", "No implicit migration"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await Db(async db =>
        {
            var row = await db.SelfDeclarations.SingleAsync(x => x.Id == legacy.Id);
            Assert.Equal(state, row.Status); Assert.Equal(legacy.RawPoints, row.RawPoints);
            Assert.False(await db.ExperienceLedgerEffects.AnyAsync(x => x.ContributionId == legacy.Id));
        });
    }

    [Theory]
    [InlineData("Submitted")]
    [InlineData("RevisionRequested")]
    public async Task OwnerCanUpgradePendingHistoryWithRealEvidenceAndReplayWithoutDuplicateRecords(string state)
    {
        await factory.EnsureStudentPolicyAsync();
        var legacy = await Seed(state); using var student = Client("student-1"); using var other = Client("student-2");
        var submission = await Submission(student);
        var input = new UpgradeDeclarationRequest(DeclarationResponse.Fingerprint(legacy), submission);
        var path = $"/api/v1/declarations/{legacy.Id}/upgrade";
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(path, input)).StatusCode);
        student.DefaultRequestHeaders.Remove("Idempotency-Key"); student.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await student.PostAsJsonAsync(path, input);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(legacy.Id, receipt.GetProperty("id").GetGuid()); Assert.Equal("SUBMITTED", receipt.GetProperty("status").GetString());
        Assert.Equal(0, receipt.GetProperty("rawPoints").GetDecimal());
        Assert.Equal(await response.Content.ReadAsStringAsync(), await (await student.PostAsJsonAsync(path, input)).Content.ReadAsStringAsync());
        await Db(async db =>
        {
            var record = await db.Contributions.SingleAsync(x => x.Id == legacy.Id);
            Assert.Equal("FA26", record.SemesterId); Assert.Contains("legacyDeclarationSnapshot", record.SourceSnapshotJson);
            Assert.Equal(legacy.CreatedAtUtc, (await db.SelfDeclarations.SingleAsync(x => x.Id == legacy.Id)).CreatedAtUtc);
            Assert.False(await db.ExperienceLedgerEffects.AnyAsync(x => x.ContributionId == legacy.Id));
            Assert.Single(await db.ContributionRevisions.Where(x => x.ContributionId == legacy.Id).ToArrayAsync());
        });
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task HistoricalDecisionsCannotBeUpgradedOrRescored(string state)
    {
        await factory.EnsureStudentPolicyAsync(); var legacy = await Seed(state); using var student = Client("student-1");
        student.DefaultRequestHeaders.Remove("Idempotency-Key"); student.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var input = new UpgradeDeclarationRequest(DeclarationResponse.Fingerprint(legacy), await Submission(student));
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync($"/api/v1/declarations/{legacy.Id}/upgrade", input)).StatusCode);
        await Db(async db => Assert.False(await db.Contributions.AnyAsync(x => x.Id == legacy.Id)));
    }

    [Fact]
    public async Task UpgradeRejectsChangedHistoricalVersionAndForeignEvidenceWithoutWritingAnything()
    {
        await factory.EnsureStudentPolicyAsync(); var legacy = await Seed("Submitted"); using var student = Client("student-1"); using var other = Client("student-2");
        var version = DeclarationResponse.Fingerprint(legacy); var submission = await Submission(student);
        await Db(async db => { (await db.SelfDeclarations.SingleAsync(x => x.Id == legacy.Id)).ReviewNote = "Changed since read"; await db.SaveChangesAsync(); });
        student.DefaultRequestHeaders.Remove("Idempotency-Key"); student.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync($"/api/v1/declarations/{legacy.Id}/upgrade", new UpgradeDeclarationRequest(version, submission))).StatusCode);
        var foreign = await Submission(other);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync($"/api/v1/declarations/{legacy.Id}/upgrade", new UpgradeDeclarationRequest(version, foreign))).StatusCode);
        await Db(async db => Assert.False(await db.Contributions.AnyAsync(x => x.Id == legacy.Id)));
    }

    private async Task<SelfDeclaration> Seed(string state)
    {
        var row = new SelfDeclaration { StudentId = 301, StudentName = "Student One", StudentEmail = "student1@fpt.edu.vn", CampusCode = "HAN", Title = "Historical " + Guid.NewGuid(), Category = "Academic", OrganizationSource = "Original organization", EvidenceUrl = "https://example.test/old-evidence", EvidenceDescription = "Original evidence context", StartDate = new(2026, 11, 18), EndDate = new(2026, 11, 18), Status = state, RawPoints = state == "Approved" ? 42 : null };
        await Db(async db => { db.SelfDeclarations.Add(row); await db.SaveChangesAsync(); await db.Entry(row).ReloadAsync(); }); return row;
    }
    private async Task<SubmitDeclarationRequest> Submission(HttpClient client)
    {
        using var upload = new MultipartFormDataContent();
        var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfO0AAAAASUVORK5CYII=")); png.Headers.ContentType = new("image/png"); upload.Add(png, "file", "historical-evidence.png");
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.PostAsync("/api/contributions/evidence", upload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Input() with { SemesterId = "FA26", OccurredAt = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero), ProposedAttributes = new("T1", "Participant", "National", "None", [new("Academic", 1)]), EvidenceRefs = [(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evidenceId").GetGuid()], PolicyVersion = factory.StudentPolicyVersion };
    }
    private static SubmitDeclarationRequest Input() => new("Declaration " + Guid.NewGuid(), "Academic", "Evidence organization", true, null, "https://example.test/old", "Actual evidence description", "Participant", new(2026, 11, 18), new(2026, 11, 18));
    private HttpClient Client(string token) { var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client; }
    private async Task Db(Func<AdminDbContext, Task> action) { await using var scope = factory.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AdminDbContext>()); }
}
