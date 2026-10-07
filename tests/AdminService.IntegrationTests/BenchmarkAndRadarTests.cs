using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using AdminService.Errors;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class BenchmarkAndRadarTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task BenchmarkEndpoints_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/student-affairs/benchmarks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BenchmarkEndpoints_StudentForbidden_Returns403()
    {
        using var client = CreateClient("student-1");
        var response = await client.GetAsync("/api/v1/student-affairs/benchmarks");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ctsv_CanCreateAndUpdateBenchmark_AndCannotUpdateWhenLocked()
    {
        using var ctsvClient = CreateClient("student-affairs");

        // 1. Create a benchmark for semester SP27
        var createRequest = new CreateBenchmarkConfigRequest(
            SemesterCode: "SP27",
            AcademicYear: "2026-2027",
            IsActive: false,
            TauAcademic: 1000m,
            TauResearch: 1200m,
            TauGlobal: 1000m,
            TauCultureSports: 800m,
            TauCommunity: 800m,
            TauEntrepreneurship: 1000m,
            TauRealWorldWork: 1500m);

        var createResponse = await ctsvClient.PostAsJsonAsync(
            "/api/v1/student-affairs/benchmarks",
            createRequest);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BenchmarkConfigResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("SP27", created.SemesterCode);
        Assert.False(created.IsLocked);
        Assert.Equal(1200m, created.TauResearch);

        // 2. Update benchmark before it is locked
        var updateRequest = new UpdateBenchmarkConfigRequest(
            IsActive: false,
            TauAcademic: 1000m,
            TauResearch: 1100m, // updated
            TauGlobal: 1000m,
            TauCultureSports: 800m,
            TauCommunity: 800m,
            TauEntrepreneurship: 1000m,
            TauRealWorldWork: 1500m);

        var updateResponse = await ctsvClient.PutAsJsonAsync(
            $"/api/v1/student-affairs/benchmarks/{created.Id}",
            updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<BenchmarkConfigResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(1100m, updated.TauResearch);

        // 3. Lock benchmark
        var lockResponse = await ctsvClient.PostAsync(
            $"/api/v1/student-affairs/benchmarks/{created.Id}/lock",
            null);

        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        var locked = await lockResponse.Content.ReadFromJsonAsync<BenchmarkConfigResponse>(JsonOptions);
        Assert.NotNull(locked);
        Assert.True(locked.IsLocked);
        Assert.NotNull(locked.LockedAtUtc);

        // 4. Attempt to update locked benchmark -> 422 UnprocessableEntity
        var tryUpdateLocked = await ctsvClient.PutAsJsonAsync(
            $"/api/v1/student-affairs/benchmarks/{created.Id}",
            updateRequest);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tryUpdateLocked.StatusCode);
        var errorJson = await tryUpdateLocked.Content.ReadFromJsonAsync<JsonElement>();
        var error = errorJson.GetProperty("error");
        Assert.Equal(ApiErrorCodes.BusinessRule, error.GetProperty("code").GetString());
        Assert.Contains("đã bị khóa", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task StudentRadar_ComputesProperlyFromApprovedDeclarations()
    {
        // 1. Student 1 queries radar initially -> Unexplored
        using var studentClient = CreateClient("student-1");
        var initialRadarResp = await studentClient.GetAsync("/api/v1/declarations/me/radar");
        Assert.Equal(HttpStatusCode.OK, initialRadarResp.StatusCode);
        var initialRadar = await initialRadarResp.Content.ReadFromJsonAsync<StudentRadarResponse>(JsonOptions);
        Assert.NotNull(initialRadar);
        Assert.Equal(301, initialRadar.StudentId);
        Assert.Equal(0m, initialRadar.D);
        Assert.Equal(0m, initialRadar.ERI);
        Assert.Equal(ExperienceTitles.Unexplored, initialRadar.ProfileTitle);

        // 2. Student submits a declaration for Academic pillar
        var submitRequest = new SubmitDeclarationRequest(
            Title: "Coursera Deep Learning Specialization",
            Category: ExperienceCategories.Academic,
            OrganizationSource: "Coursera",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://coursera.org/verify/123",
            EvidenceDescription: "Certificate of completion",
            RoleProposed: ContributionRoles.Contributor,
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 9, 20));

        var submitResp = await studentClient.PostAsJsonAsync("/api/v1/declarations", submitRequest);
        var declaration = await submitResp.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(declaration);

        // 3. CTSV approves declaration: Tier T3 (60) * Contributor (1.6) * Club (1.0) = 96.0 points
        using var ctsvClient = CreateClient("student-affairs");
        var reviewRequest = new ReviewDeclarationRequest(
            Decision: DeclarationDecisions.Approved,
            ReviewNote: "Verified Coursera credential ID",
            Tier: EffortTiers.T3,
            Role: ContributionRoles.Contributor,
            Scale: ActivityScales.Club);

        var reviewResp = await ctsvClient.PostAsJsonAsync(
            $"/api/v1/student-affairs/declarations/{declaration.Id}/review",
            reviewRequest);
        Assert.Equal(HttpStatusCode.OK, reviewResp.StatusCode);

        // 4. Student queries radar again -> now has Academic score and profile title
        var afterRadarResp = await studentClient.GetAsync("/api/v1/declarations/me/radar");
        Assert.Equal(HttpStatusCode.OK, afterRadarResp.StatusCode);
        var afterRadar = await afterRadarResp.Content.ReadFromJsonAsync<StudentRadarResponse>(JsonOptions);
        Assert.NotNull(afterRadar);
        Assert.Equal(1, afterRadar.ApprovedDeclarationsCount);
        Assert.True(afterRadar.D > 0m);
        Assert.True(afterRadar.ERI > 0m);
        Assert.Equal("Thế mạnh Academic", afterRadar.ProfileTitle);

        var academicPillar = Assert.Single(afterRadar.Pillars, p => p.Pillar == ExperienceCategories.Academic);
        Assert.Equal(96.0m, academicPillar.RawPointsSum);
        Assert.True(academicPillar.SaturatedScore > 0m);

        // 5. CTSV inspects student radar via /student-affairs/students/301/radar
        var ctsvInspectResp = await ctsvClient.GetAsync("/api/v1/student-affairs/students/301/radar?semester=FA26");
        Assert.Equal(HttpStatusCode.OK, ctsvInspectResp.StatusCode);
        var ctsvInspect = await ctsvInspectResp.Content.ReadFromJsonAsync<StudentRadarResponse>(JsonOptions);
        Assert.NotNull(ctsvInspect);
        Assert.Equal(301, ctsvInspect.StudentId);
        Assert.Equal("FA26", ctsvInspect.SemesterCode);
        Assert.Equal(afterRadar.ERI, ctsvInspect.ERI);

        // Student 2 cannot inspect Student 1's radar via staff endpoint -> 403 Forbidden
        using var student2Client = CreateClient("student-2");
        var student2Inspect = await student2Client.GetAsync("/api/v1/student-affairs/students/301/radar");
        Assert.Equal(HttpStatusCode.Forbidden, student2Inspect.StatusCode);
    }

    [Fact]
    public async Task RadarOverview_StudentForbidden_Returns403()
    {
        using var studentClient = CreateClient("student-1");
        var response = await studentClient.GetAsync("/api/v1/student-affairs/radar/overview");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RadarOverview_Ctsv_CanViewOwnCampus_AndForbiddenOnOtherCampus()
    {
        using var ctsvHanClient = CreateClient("student-affairs-han");

        // 1. CTSV HAN views overview -> succeeds and scopes to HAN
        var response = await ctsvHanClient.GetAsync("/api/v1/student-affairs/radar/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var overview = await response.Content.ReadFromJsonAsync<CampusRadarOverviewResponse>(JsonOptions);
        Assert.NotNull(overview);
        Assert.Equal("CAMPUS", overview.Scope);
        Assert.Equal(CampusCodes.Hanoi, overview.CampusCode);
        Assert.Equal("Hà Nội (Hòa Lạc)", overview.CampusName);
        Assert.NotNull(overview.Pillars);
        Assert.Equal(7, overview.Pillars.Count);

        // Verify the 6 core pillars reflect documented functions
        var academic = Assert.Single(overview.Pillars, p => p.Pillar == ExperienceCategories.Academic);
        Assert.Equal("Học tập", academic.PillarName);
        Assert.False(string.IsNullOrWhiteSpace(academic.Description));
        Assert.NotNull(academic.MasteryTierCounts);

        var research = Assert.Single(overview.Pillars, p => p.Pillar == ExperienceCategories.Research);
        Assert.Equal("Nghiên cứu", research.PillarName);

        var globalPillar = Assert.Single(overview.Pillars, p => p.Pillar == ExperienceCategories.Global);
        Assert.Equal("Quốc tế", globalPillar.PillarName);

        var cultureSports = Assert.Single(overview.Pillars, p => p.Pillar == ExperienceCategories.CultureSports);
        Assert.Equal("Thể thao & văn hóa", cultureSports.PillarName);

        var community = Assert.Single(overview.Pillars, p => p.Pillar == ExperienceCategories.Community);
        Assert.Equal("Cộng đồng", community.PillarName);

        var entrepreneurship = Assert.Single(overview.Pillars, p => p.Pillar == ExperienceCategories.Entrepreneurship);
        Assert.Equal("Khởi nghiệp", entrepreneurship.PillarName);

        var realWorld = Assert.Single(overview.Pillars, p => p.Pillar == ExperienceCategories.RealWorldWork);
        Assert.Equal("Thực chiến dự án (+1)", realWorld.PillarName);

        // CTSV cannot query another campus (e.g. HCM) -> 403 Forbidden
        var forbiddenResp = await ctsvHanClient.GetAsync("/api/v1/student-affairs/radar/overview?campusCode=HCM");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);
    }

    [Fact]
    public async Task RadarOverview_Admin_CanViewGlobalOverview_AndSpecificCampus()
    {
        using var adminClient = CreateClient("admin");

        // 1. Admin views global overview -> Scope GLOBAL, includes CampusesComparison for all 5 FPT campuses
        var globalResp = await adminClient.GetAsync("/api/v1/student-affairs/radar/overview");
        Assert.Equal(HttpStatusCode.OK, globalResp.StatusCode);

        var globalOverview = await globalResp.Content.ReadFromJsonAsync<CampusRadarOverviewResponse>(JsonOptions);
        Assert.NotNull(globalOverview);
        Assert.Equal("GLOBAL", globalOverview.Scope);
        Assert.Equal(CampusCodes.Global, globalOverview.CampusCode);
        Assert.NotNull(globalOverview.CampusesComparison);
        Assert.Equal(5, globalOverview.CampusesComparison.Count);
        Assert.Contains(globalOverview.CampusesComparison, c => c.CampusCode == CampusCodes.Hanoi);
        Assert.Contains(globalOverview.CampusesComparison, c => c.CampusCode == CampusCodes.HoChiMinh);
        Assert.Contains(globalOverview.CampusesComparison, c => c.CampusCode == CampusCodes.Danang);
        Assert.Contains(globalOverview.CampusesComparison, c => c.CampusCode == CampusCodes.CanTho);
        Assert.Contains(globalOverview.CampusesComparison, c => c.CampusCode == CampusCodes.QuyNhon);

        // 2. Admin views specific campus (e.g. HCM) -> Scope CAMPUS, CampusCode HCM
        var hcmResp = await adminClient.GetAsync("/api/v1/student-affairs/radar/overview?campusCode=HCM");
        Assert.Equal(HttpStatusCode.OK, hcmResp.StatusCode);

        var hcmOverview = await hcmResp.Content.ReadFromJsonAsync<CampusRadarOverviewResponse>(JsonOptions);
        Assert.NotNull(hcmOverview);
        Assert.Equal("CAMPUS", hcmOverview.Scope);
        Assert.Equal(CampusCodes.HoChiMinh, hcmOverview.CampusCode);
        Assert.Equal("TP. Hồ Chí Minh", hcmOverview.CampusName);
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
