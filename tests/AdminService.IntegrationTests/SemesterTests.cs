using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class SemesterTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetSemesters_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/semesters");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSemesters_StudentForbidden_Returns403()
    {
        using var client = CreateClient("student-1");

        var response = await client.GetAsync("/api/v1/semesters");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanGetSemesters_AndSeedsDefaults()
    {
        using var client = CreateClient("admin");

        var response = await client.GetAsync("/api/v1/semesters");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var semesters = await response.Content.ReadFromJsonAsync<IReadOnlyList<SemesterResponse>>(JsonOptions);
        Assert.NotNull(semesters);
        Assert.NotEmpty(semesters);
        Assert.Contains(semesters, s => s.SemesterCode == "FALL2026");
    }

    [Fact]
    public async Task Admin_CanGetActiveSemester()
    {
        using var client = CreateClient("admin");

        var response = await client.GetAsync("/api/v1/semesters/active");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var active = await response.Content.ReadFromJsonAsync<SemesterResponse>(JsonOptions);
        Assert.NotNull(active);
        Assert.True(active.IsActive);
    }

    [Fact]
    public async Task Admin_CanCreateAndUpdateSemester_ValidatesAndPersists()
    {
        using var client = CreateClient("admin");

        var uniqueCode = "SP" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var createReq = new CreateSemesterRequest(
            SemesterCode: uniqueCode,
            Label: "Spring Test",
            AcademicYear: "2026-2027",
            Start: "2027-01-10",
            End: "2027-05-10",
            Threshold: 180,
            XpPerLevel: 90,
            Rankings: true,
            IsActive: false);

        var createRes = await client.PostAsJsonAsync("/api/v1/semesters", createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var created = await createRes.Content.ReadFromJsonAsync<SemesterResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(uniqueCode, created.SemesterCode);
        Assert.Equal("Spring Test", created.Label);
        Assert.Equal(180, created.Threshold);
        Assert.Equal(90, created.XpPerLevel);
        Assert.False(created.IsActive);

        // Update semester
        var updateReq = new UpdateSemesterRequest(
            Label: "Spring Test Updated",
            Threshold: 220,
            XpPerLevel: 110,
            Rankings: false,
            IsActive: true);

        var updateRes = await client.PutAsJsonAsync($"/api/v1/semesters/{created.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var updated = await updateRes.Content.ReadFromJsonAsync<SemesterResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Spring Test Updated", updated.Label);
        Assert.Equal(220, updated.Threshold);
        Assert.Equal(110, updated.XpPerLevel);
        Assert.False(updated.Rankings);
        Assert.True(updated.IsActive);

        // Active semester check
        var activeRes = await client.GetAsync("/api/v1/semesters/active");
        Assert.Equal(HttpStatusCode.OK, activeRes.StatusCode);
        var active = await activeRes.Content.ReadFromJsonAsync<SemesterResponse>(JsonOptions);
        Assert.NotNull(active);
        Assert.Equal(uniqueCode, active.SemesterCode);
    }

    [Fact]
    public async Task DirectAliases_WorkCorrectly()
    {
        using var client = CreateClient("admin");

        var response = await client.GetAsync("/api/semesters");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var activeResponse = await client.GetAsync("/api/semesters/active");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
    }

    private HttpClient CreateClient(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", role);
        return client;
    }
}
