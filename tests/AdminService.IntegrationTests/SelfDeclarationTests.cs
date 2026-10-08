using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminService.Contracts;
using AdminService.Errors;
using ClubReportHub.Shared.Experience;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminService.IntegrationTests;

public sealed class SelfDeclarationTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SubmitDeclaration_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        var request = new SubmitDeclarationRequest(
            Title: "Participated in Global Contest",
            Category: ExperienceCategories.Global,
            OrganizationSource: "Google Developer Groups",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/certificate.pdf",
            EvidenceDescription: "Certificate of participation",
            RoleProposed: ContributionRoles.Contributor,
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 9, 3));

        var response = await client.PostAsJsonAsync("/api/v1/declarations", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubmitDeclaration_WithValidStudentToken_Returns201AndStoredInPersonalQueue()
    {
        using var client = CreateClient("student-1");
        var request = new SubmitDeclarationRequest(
            Title: "Google Developer Hackathon 2026",
            Category: ExperienceCategories.Global,
            OrganizationSource: "Google Developer Groups",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/certificate.pdf",
            EvidenceDescription: "Certificate of participation and source repository",
            RoleProposed: ContributionRoles.Contributor,
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 9, 3));

        var response = await client.PostAsJsonAsync("/api/v1/declarations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("Google Developer Hackathon 2026", created.Title);
        Assert.Equal(ExperienceCategories.Global, created.Category);
        Assert.Equal(DeclarationStatuses.Submitted, created.Status);
        Assert.Equal(301, created.StudentId);
        Assert.Equal("student1@fpt.edu.vn", created.StudentEmail);

        // Fetch personal declarations
        var meResponse = await client.GetAsync("/api/v1/declarations/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var myList = await meResponse.Content.ReadFromJsonAsync<List<DeclarationResponse>>(JsonOptions);
        Assert.NotNull(myList);
        Assert.Contains(myList, d => d.Id == created.Id);

        // Student 2 cannot see Student 1's declaration in /me
        using var client2 = CreateClient("student-2");
        var student2Me = await client2.GetAsync("/api/v1/declarations/me");
        var student2List = await student2Me.Content.ReadFromJsonAsync<List<DeclarationResponse>>(JsonOptions);
        Assert.NotNull(student2List);
        Assert.DoesNotContain(student2List, d => d.Id == created.Id);

        // Student 2 cannot fetch Student 1's declaration by ID (403 Forbidden)
        var student2Detail = await client2.GetAsync($"/api/v1/declarations/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, student2Detail.StatusCode);
    }

    [Fact]
    public async Task SubmitDeclaration_MissingEvidence_Returns400()
    {
        using var client = CreateClient("student-1");
        var invalidRequest = new SubmitDeclarationRequest(
            Title: "Valid Title",
            Category: ExperienceCategories.Academic,
            OrganizationSource: "Coursera",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "", // Missing
            EvidenceDescription: "Completed course",
            RoleProposed: null,
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 9, 3));

        var response = await client.PostAsJsonAsync("/api/v1/declarations", invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CtsvQueue_OnlyAccessibleByStudentAffairsOrAdmin()
    {
        // Student attempting to view CTSV queue -> 403 Forbidden
        using var studentClient = CreateClient("student-1");
        var studentResponse = await studentClient.GetAsync("/api/v1/student-affairs/declarations");
        Assert.Equal(HttpStatusCode.Forbidden, studentResponse.StatusCode);

        // CTSV viewing queue -> 200 OK
        using var ctsvClient = CreateClient("student-affairs");
        var ctsvResponse = await ctsvClient.GetAsync("/api/v1/student-affairs/declarations");
        Assert.Equal(HttpStatusCode.OK, ctsvResponse.StatusCode);
    }

    [Fact]
    public async Task CtsvReview_ApproveDeclaration_CalculatesPointsAccurately()
    {
        // 1. Student submits a national contest declaration
        using var studentClient = CreateClient("student-1");
        var submitRequest = new SubmitDeclarationRequest(
            Title: "Vietnam National AI Hackathon 2026",
            Category: ExperienceCategories.Research,
            OrganizationSource: "Ministry of Science & Tech",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/award_finalist.pdf",
            EvidenceDescription: "Reached top 10 finalists nationwide",
            RoleProposed: ContributionRoles.Contributor,
            StartDate: new DateOnly(2026, 9, 10),
            EndDate: new DateOnly(2026, 9, 12));

        var submitResponse = await studentClient.PostAsJsonAsync("/api/v1/declarations", submitRequest);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var declaration = await submitResponse.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(declaration);

        // 2. CTSV reviews and approves with rubric:
        // Tier T4 (150) * Contributor (1.6) * National (2.2) * (1 + Finalist 0.3) = 686.4
        using var ctsvClient = CreateClient("student-affairs");
        var reviewRequest = new ReviewDeclarationRequest(
            Decision: DeclarationDecisions.Approved,
            ReviewNote: "Verified certificate against Ministry registry.",
            FinalCategory: ExperienceCategories.Research,
            Tier: EffortTiers.T4,
            Role: ContributionRoles.Contributor,
            Scale: ActivityScales.National,
            BonusResult: BonusResults.Finalist);

        var reviewResponse = await ctsvClient.PostAsJsonAsync(
            $"/api/v1/student-affairs/declarations/{declaration.Id}/review",
            reviewRequest);

        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(reviewed);
        Assert.Equal(DeclarationStatuses.Approved, reviewed.Status);
        Assert.Equal(686.4m, reviewed.RawPoints);
        Assert.Equal("Verified certificate against Ministry registry.", reviewed.ReviewNote);
        Assert.Equal(EffortTiers.T4, reviewed.Tier);
        Assert.Equal(ContributionRoles.Contributor, reviewed.Role);
        Assert.Equal(ActivityScales.National, reviewed.Scale);
        Assert.Equal(BonusResults.Finalist, reviewed.BonusResult);
        Assert.NotNull(reviewed.ReviewedAtUtc);
    }

    [Fact]
    public async Task CtsvReview_RejectDeclaration_UpdatesStatus()
    {
        // 1. Student submits
        using var studentClient = CreateClient("student-1");
        var submitRequest = new SubmitDeclarationRequest(
            Title: "Unverified external activity",
            Category: ExperienceCategories.Community,
            OrganizationSource: "Unknown",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/broken_link",
            EvidenceDescription: "Volunteering without proof",
            RoleProposed: null,
            StartDate: new DateOnly(2026, 8, 1),
            EndDate: new DateOnly(2026, 8, 1));

        var submitResponse = await studentClient.PostAsJsonAsync("/api/v1/declarations", submitRequest);
        var declaration = await submitResponse.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(declaration);

        // 2. CTSV rejects with explanation
        using var ctsvClient = CreateClient("student-affairs");
        var reviewRequest = new ReviewDeclarationRequest(
            Decision: DeclarationDecisions.Rejected,
            ReviewNote: "Link is broken and no verifiable signature found.");

        var reviewResponse = await ctsvClient.PostAsJsonAsync(
            $"/api/v1/student-affairs/declarations/{declaration.Id}/review",
            reviewRequest);

        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(reviewed);
        Assert.Equal(DeclarationStatuses.Rejected, reviewed.Status);
        Assert.Null(reviewed.RawPoints);
        Assert.Equal("Link is broken and no verifiable signature found.", reviewed.ReviewNote);
    }

    [Fact]
    public async Task CtsvReview_SelfApprovalPrevention_EnforcesSegregationOfDuties()
    {
        // CTSV user (Id: 202) submits their own declaration as a student / learner
        using var ctsvAsStudent = CreateClient("student-affairs");
        var submitRequest = new SubmitDeclarationRequest(
            Title: "Self submitted activity by CTSV officer",
            Category: ExperienceCategories.Academic,
            OrganizationSource: "Coursera",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/cert",
            EvidenceDescription: "Certificate of completion",
            RoleProposed: ContributionRoles.Participant,
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 9, 10));

        var submitResponse = await ctsvAsStudent.PostAsJsonAsync("/api/v1/declarations", submitRequest);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var declaration = await submitResponse.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(declaration);

        // The same CTSV officer (Id: 202) attempts to review their own submission -> 422 UnprocessableEntity
        var reviewRequest = new ReviewDeclarationRequest(
            Decision: DeclarationDecisions.Approved,
            ReviewNote: "Approving my own declaration",
            Tier: EffortTiers.T1,
            Role: ContributionRoles.Participant,
            Scale: ActivityScales.Club);

        var reviewResponse = await ctsvAsStudent.PostAsJsonAsync(
            $"/api/v1/student-affairs/declarations/{declaration.Id}/review",
            reviewRequest);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reviewResponse.StatusCode);
        var json = await reviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error");
        Assert.Equal(ApiErrorCodes.BusinessRule, error.GetProperty("code").GetString());
        Assert.Contains("không được tự thẩm định", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Admin_CanAlsoAccessCtsvQueue()
    {
        using var adminClient = CreateClient("admin");
        var response = await adminClient.GetAsync("/api/v1/student-affairs/declarations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CtsvReview_CannotReapproveAlreadyApprovedDeclaration()
    {
        // 1. Submit
        using var studentClient = CreateClient("student-1");
        var submitRequest = new SubmitDeclarationRequest(
            Title: "Initial Submission",
            Category: ExperienceCategories.Academic,
            OrganizationSource: "Coursera",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/cert",
            EvidenceDescription: "Certificate",
            RoleProposed: ContributionRoles.Participant,
            StartDate: new DateOnly(2026, 9, 1),
            EndDate: new DateOnly(2026, 9, 2));

        var submitResponse = await studentClient.PostAsJsonAsync("/api/v1/declarations", submitRequest);
        var declaration = await submitResponse.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(declaration);

        // 2. Approve first time
        using var ctsvClient = CreateClient("student-affairs");
        var reviewRequest = new ReviewDeclarationRequest(
            Decision: DeclarationDecisions.Approved,
            ReviewNote: "First approval",
            Tier: EffortTiers.T1,
            Role: ContributionRoles.Participant,
            Scale: ActivityScales.Club);

        var firstReview = await ctsvClient.PostAsJsonAsync(
            $"/api/v1/student-affairs/declarations/{declaration.Id}/review",
            reviewRequest);
        Assert.Equal(HttpStatusCode.OK, firstReview.StatusCode);

        // 3. Second review attempt -> 422 UnprocessableEntity
        var secondReview = await ctsvClient.PostAsJsonAsync(
            $"/api/v1/student-affairs/declarations/{declaration.Id}/review",
            reviewRequest);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, secondReview.StatusCode);
        var json = await secondReview.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error");
        Assert.Equal(ApiErrorCodes.BusinessRule, error.GetProperty("code").GetString());
        Assert.Contains("đã được phê duyệt", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Student_CanUpdateDeclaration_AndOtherStudentForbidden()
    {
        // 1. Student 1 submits
        using var student1 = CreateClient("student-1");
        var submitRequest = new SubmitDeclarationRequest(
            Title: "Original Title",
            Category: ExperienceCategories.Community,
            OrganizationSource: "Red Cross",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/v1",
            EvidenceDescription: "Original description",
            RoleProposed: ContributionRoles.Participant,
            StartDate: new DateOnly(2026, 8, 1),
            EndDate: new DateOnly(2026, 8, 2));

        var submitResp = await student1.PostAsJsonAsync("/api/v1/declarations", submitRequest);
        var created = await submitResp.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(created);

        // 2. Student 2 tries to update -> 403 Forbidden
        using var student2 = CreateClient("student-2");
        var updateRequest = submitRequest with { Title = "Hacked Title" };
        var student2UpdateResp = await student2.PutAsJsonAsync($"/api/v1/declarations/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, student2UpdateResp.StatusCode);

        // 3. Student 1 updates successfully -> 200 OK
        var legitimateUpdate = submitRequest with
        {
            Title = "Updated Title with Better Proof",
            EvidenceUrl = "https://example.com/v2-updated"
        };
        var updateResp = await student1.PutAsJsonAsync($"/api/v1/declarations/{created.Id}", legitimateUpdate);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated Title with Better Proof", updated.Title);
        Assert.Equal("https://example.com/v2-updated", updated.EvidenceUrl);
    }

    [Fact]
    public async Task Ctsv_CanReview_WithNeedClarification_And_DefaultApproval()
    {
        using var student = CreateClient("student-1");
        var submitRequest = new SubmitDeclarationRequest(
            Title: "Clarification Test Event",
            Category: ExperienceCategories.Academic,
            OrganizationSource: "Coursera",
            IsOutsideClub: true,
            ClubId: null,
            EvidenceUrl: "https://example.com/cert",
            EvidenceDescription: "Certificate proof",
            RoleProposed: ContributionRoles.Participant,
            StartDate: new DateOnly(2026, 8, 1),
            EndDate: new DateOnly(2026, 8, 2));

        var submitResp = await student.PostAsJsonAsync("/api/v1/declarations", submitRequest);
        var created = await submitResp.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(created);

        // Review with NEED_CLARIFICATION (alias for RevisionRequested used by DeclarationsQueue.jsx)
        using var ctsv = CreateClient("student-affairs");
        var clarifyReq = new ReviewDeclarationRequest(
            Decision: "NEED_CLARIFICATION",
            ReviewNote: "Vui lòng đính kèm chứng chỉ định dạng PDF có mã xác thực.");

        var clarifyResp = await ctsv.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{created.Id}/review", clarifyReq);
        Assert.Equal(HttpStatusCode.OK, clarifyResp.StatusCode);

        var clarified = await clarifyResp.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(clarified);
        Assert.Equal(DeclarationStatuses.RevisionRequested, clarified.Status);

        // Student updates
        await student.PutAsJsonAsync($"/api/v1/declarations/{created.Id}", submitRequest with { EvidenceUrl = "https://example.com/valid.pdf" });

        // CTSV Approves without explicit tier/role/scale (defaults applied gracefully)
        var approveReq = new ReviewDeclarationRequest(
            Decision: "APPROVED",
            ReviewNote: "Hồ sơ đã đầy đủ chứng chỉ hợp lệ.");

        var approveResp = await ctsv.PostAsJsonAsync($"/api/v1/student-affairs/declarations/{created.Id}/review", approveReq);
        Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);

        var approved = await approveResp.Content.ReadFromJsonAsync<DeclarationResponse>(JsonOptions);
        Assert.NotNull(approved);
        Assert.Equal(DeclarationStatuses.Approved, approved.Status);
        Assert.NotNull(approved.RawPoints);
        Assert.True(approved.RawPoints > 0);
    }

    private HttpClient CreateClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
