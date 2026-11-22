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

public sealed class CampusDeclarationLifecycleTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Academic")]
    [InlineData("RealWorldWork")]
    public async Task DirectDeclaration_UploadSubmitRevisionApproveAndReplay_AwardsExactlyOnce(string pillar)
    {
        await factory.EnsureStudentPolicyAsync();
        using var student = Client("student-1");
        using var staff = Client("student-affairs-han");
        var request = await Submission(student, pillar);
        Key(student);
        var submitted = await student.PostAsJsonAsync("/api/v1/declarations", request);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var original = (await submitted.Content.ReadFromJsonAsync<ContributionResponse>(Json))!;
        var id = original.Id;
        Assert.Equal("SUBMITTED", original.Status);
        Assert.Equal(0, original.RawPoints);

        var queue = await staff.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/declarations?status=Submitted&pageSize=100");
        Assert.Contains(queue.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/contributions/evidence/{request.EvidenceRefs![0]}")).StatusCode);

        Key(staff);
        var clarification = await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review",
            new { decision = "REQUEST_REVISION", reason = "Add the result description", expectedVersion = original.Version });
        Assert.True(clarification.IsSuccessStatusCode, await clarification.Content.ReadAsStringAsync());
        var revisedVersion = (await clarification.Content.ReadFromJsonAsync<ContributionResponse>(Json))!.Version;
        Assert.NotEqual(original.Version, revisedVersion);

        Key(student);
        var revised = await student.PutAsJsonAsync($"/api/v1/declarations/{id}", request with
        {
            ExpectedVersion = revisedVersion,
            EvidenceDescription = "Updated evidence and result after CTSV clarification"
        });
        Assert.True(revised.IsSuccessStatusCode, await revised.Content.ReadAsStringAsync());
        var pending = (await revised.Content.ReadFromJsonAsync<ContributionResponse>(Json))!;
        Assert.Equal("SUBMITTED", pending.Status);
        Assert.Equal(0, pending.RawPoints);
        var decision = new { decision = "APPROVE", reason = "Verified external evidence", expectedVersion = pending.Version, verifiedAttributes = request.ProposedAttributes };
        Key(staff);
        var approved = await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review", decision);
        Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
        var awarded = (await approved.Content.ReadFromJsonAsync<ContributionResponse>(Json))!;
        Assert.Equal("APPROVED", awarded.Status);
        Assert.True(awarded.RawPoints > 0);

        var replay = await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review", decision);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(awarded.Version, (await replay.Content.ReadFromJsonAsync<ContributionResponse>(Json))!.Version);
        Key(staff);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{id}/review", decision)).StatusCode);
        var detail = await student.GetFromJsonAsync<JsonElement>($"/api/v1/declarations/{id}");
        Assert.Equal("APPROVED", detail.GetProperty("contribution").GetProperty("status").GetString());
        Assert.Equal(awarded.RawPoints, detail.GetProperty("contribution").GetProperty("rawPoints").GetDecimal());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var effect = Assert.Single(await db.ExperienceLedgerEffects.Where(x => x.ContributionId == id).ToListAsync());
        Assert.Equal(awarded.RawPoints, effect.Amount);
        Assert.Equal("CTSV_DECLARATION_REVIEW", effect.VerificationMethod);
        Assert.Equal("Approved", (await db.SelfDeclarations.SingleAsync(x => x.Id == id)).Status);
        Assert.Equal(2, await db.ContributionDecisions.CountAsync(x => x.ContributionId == id));
    }

    [Theory]
    [InlineData("Academic")]
    [InlineData("RealWorldWork")]
    public async Task DirectDeclaration_RejectsWithoutCreatingAnyXpEffect(string pillar)
    {
        await factory.EnsureStudentPolicyAsync();
        using var student = Client("student-1");
        using var staff = Client("student-affairs-han");
        var request = await Submission(student, pillar);
        Key(student);
        var submitted = await student.PostAsJsonAsync("/api/v1/declarations", request);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var original = (await submitted.Content.ReadFromJsonAsync<ContributionResponse>(Json))!;
        Key(staff);
        var rejected = await staff.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{original.Id}/review",
            new { decision = "REJECT", reason = "Evidence does not establish the claimed result", expectedVersion = original.Version });
        Assert.True(rejected.IsSuccessStatusCode, await rejected.Content.ReadAsStringAsync());
        Assert.Equal("REJECTED", (await rejected.Content.ReadFromJsonAsync<ContributionResponse>(Json))!.Status);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        Assert.False(await db.ExperienceLedgerEffects.AnyAsync(x => x.ContributionId == original.Id));
        Assert.Equal("Rejected", (await db.SelfDeclarations.SingleAsync(x => x.Id == original.Id)).Status);
    }

    private async Task<SubmitDeclarationRequest> Submission(HttpClient student, string pillar)
    {
        using var upload = new MultipartFormDataContent();
        var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfO0AAAAASUVORK5CYII="));
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        upload.Add(png, "file", "qa-lifecycle.png");
        Key(student);
        var uploaded = await student.PostAsync("/api/contributions/evidence", upload);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var evidence = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evidenceId").GetGuid();
        return new SubmitDeclarationRequest($"QA full lifecycle {Guid.NewGuid()}", pillar, "QA organization", true, null,
            "", "QA evidence description", "Participant", new(2026, 11, 18), new(2026, 11, 18),
            SemesterId: "FA26", OccurredAt: new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero),
            ProposedAttributes: new("T1", "Participant", "National", "None", [new(pillar, 1)]),
            EvidenceRefs: [evidence], PolicyVersion: factory.StudentPolicyVersion);
    }

    private static void Key(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
