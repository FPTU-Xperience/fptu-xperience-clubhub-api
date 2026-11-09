using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using ReportService.Clients;
using ReportService.Data;
using ReportService.Endpoints;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ClubReportHub.Shared.Auth;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReportService.Extensions;
using ReportService.Models;

namespace Backend.StabilizationTests;
public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task Governance_UploadedSecretaryReportCannotBypassExplicitDraftOnlyScope()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = SigningKey });
        var name = Guid.NewGuid().ToString(); builder.Services.AddDbContext<ReportDbContext>(options => options.UseInMemoryDatabase(name));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(CreateClubAccessClient(new ClubAccessSnapshot(1, "Club", false, false, true, [100]) { Capabilities = ["club.reports.draft.write"], AccessValidUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10) }));
        builder.Services.AddSingleton(new FinanceWorkflowClient(CreateFakeHttpClient(_ => new(HttpStatusCode.NotFound)), NullLogger<FinanceWorkflowClient>.Instance));
        builder.Services.AddSingleton(CreateActivityPublishingClient());
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapGroup("/api/reports").RequireAuthorization().MapReportWorkflowEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportDbContext>(); db.Reports.Add(new Report { ClubId = 1, CreatedByUserId = 104, ContentSource = ReportContentSources.UploadedFile, ReportType = "CLUB_TERM_START", Tag = "CLUB_TERM_START", Period = "FA26" }); await db.SaveChangesAsync();
        }
        await app.StartAsync(); using var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(104, AuthRoles.ClubMember));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/reports/1/submit", new { })).StatusCode);
        await using var finalScope = app.Services.CreateAsyncScope(); Assert.Equal(ReportStatuses.Draft, (await finalScope.ServiceProvider.GetRequiredService<ReportDbContext>().Reports.SingleAsync()).Status);
    }

    [Fact]
    public async Task Governance_SecretaryAndFinanceDraftCapabilitiesStayOwnResourceAndCannotSubmitOrBecomeLeadership()
    {
        var snapshot = new ClubAccessSnapshot(1, "Club", false, false, true, [100]) { Capabilities = ["club.reports.draft.write"], AccessValidUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ClubAccessClient(CreateFakeHttpClient(_ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { snapshot }) }), cache, NullLogger<ClubAccessClient>.Instance);
        var http = new DefaultHttpContext(); http.Request.Headers.Authorization = "Bearer " + CreateToken(104, AuthRoles.ClubMember);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "104") }, "Test"));
        var report = new Report { ClubId = 1, CreatedByUserId = 104, Tag = "CLUB_TERM_START", ReportType = "CLUB_TERM_START", Period = "FA26" };
        Assert.NotNull(await ReportExtensions.GetDraftAuthorAccessAsync(1, report.Tag, report.ReportType, client, http, default));
        Assert.Null(await ReportExtensions.GetAuthorAccessAsync(1, report.Tag, report.ReportType, client, http, default));
        Assert.True(await ReportExtensions.CanEditDraftReportAsync(report, user, client, http, default));
        report.CreatedByUserId = 105; Assert.False(await ReportExtensions.CanEditDraftReportAsync(report, user, client, http, default));
        report.CreatedByUserId = 104; report.ReportType = "FINANCIAL";
        Assert.False(await ReportExtensions.CanEditDraftReportAsync(report, user, client, http, default));
        snapshot = snapshot with { Capabilities = ["club.finance.read"] };
        Assert.False(await ReportExtensions.CanEditDraftReportAsync(report, user, client, http, default));
        snapshot = snapshot with { Capabilities = ["club.finance.draft.write"] };
        Assert.True(await ReportExtensions.CanEditDraftReportAsync(report, user, client, http, default));
        Assert.Null(await ReportExtensions.GetAuthorAccessAsync(1, report.Tag, report.ReportType, client, http, default));
        Assert.False(snapshot.CanManage); Assert.False(snapshot.CanManageFinance);
        snapshot = snapshot with { Capabilities = ["club.finance.submit"] };
        Assert.NotNull(await ReportExtensions.GetAuthorAccessAsync(1, report.Tag, report.ReportType, client, http, default));
        Assert.False(await ReportExtensions.CanEditDraftReportAsync(report, user, client, http, default));
        snapshot = snapshot with { AccessValidUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1) };
        Assert.Null(await ReportExtensions.GetAuthorAccessAsync(1, report.Tag, report.ReportType, client, http, default));
    }

    [Fact]
    public async Task Governance_TransferNeedsCurrentIndependentCampusReviewerAndCannotPromoteTreasurer()
    {
        var state = new GovernanceState(); await using var app = await GovernanceApp(state);
        using var owner = FormationClient(app, 100, AuthRoles.ClubMember, "HAN");
        using var wrongCampus = FormationClient(app, 201, AuthRoles.StudentAffairsAdmin, "HAN");
        using var reviewer = FormationClient(app, 200, AuthRoles.StudentAffairsAdmin, "HAN");
        var transfer = await GovernanceWrite(owner, "/api/clubs/1/transfer-ownership", new TransferOwnershipRequest(104, "Transfer to approved member"), HttpStatusCode.Created);
        var path = "/api/clubs/transfer-requests/" + transfer.GetProperty("id").GetInt32() + "/approve";
        await GovernanceWrite(wrongCampus, path, new ApproveTransferRequest("Wrong current campus"), HttpStatusCode.Forbidden);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var member = await db.ClubMemberships.SingleAsync(x => x.UserId == 104); member.Role = ClubMemberRoles.Treasurer; await db.SaveChangesAsync();
        }
        await GovernanceWrite(reviewer, path, new ApproveTransferRequest("Treasurer conflict"), HttpStatusCode.Conflict);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var member = await db.ClubMemberships.SingleAsync(x => x.UserId == 104); member.Role = ClubMemberRoles.Member; await db.SaveChangesAsync();
        }
        await GovernanceWrite(reviewer, path, new ApproveTransferRequest("Independent current campus approval"));
        await using var finalScope = app.Services.CreateAsyncScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<ClubDbContext>();
        Assert.Equal(104, (await finalDb.ClubManagerAssignments.SingleAsync(x => x.IsActive)).ManagerUserId);
        Assert.Contains(await finalDb.Set<ClubGovernanceAuditEvent>().ToListAsync(), x => x.Action == "OWNER_TRANSFER_APPROVED");
    }
}
