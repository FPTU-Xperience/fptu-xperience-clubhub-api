using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Auth;
using FinanceService.Data;
using FinanceService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task FinanceWorkflow_ProposalSubmitOnlyAcknowledgesExistingSubmittedState()
    {
        await using var app = await CreateCompatibilityAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var proposal = await scope.ServiceProvider.GetRequiredService<FinanceDbContext>()
                .BudgetProposals.SingleAsync(x => x.Id == 1);
            proposal.Status = FinanceStatuses.Submitted;
            await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().SaveChangesAsync();
        }
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var response = await manager.PostAsync("/api/finance/proposals/1/submit", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("pending_manager_review", body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await manager.PostAsync("/api/finance/proposals/2/submit", null)).StatusCode);
        await using var verify = app.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(FinanceStatuses.Submitted, (await db.BudgetProposals.SingleAsync(x => x.Id == 1)).Status);
        Assert.Empty(await db.FinanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task FinanceWorkflow_ManagerAndFinalReviewOnlyAddNotes()
    {
        await using var app = await CreateCompatibilityAppAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            (await db.BudgetProposals.SingleAsync(x => x.Id == 1)).Status = FinanceStatuses.Submitted;
            (await db.BudgetProposals.SingleAsync(x => x.Id == 2)).Status = FinanceStatuses.ManagerApproved;
            (await db.BudgetProposals.SingleAsync(x => x.Id == 3)).Status = FinanceStatuses.Submitted;
            (await db.BudgetProposals.SingleAsync(x => x.Id == 3)).SourceReportId = 99;
            await db.SaveChangesAsync();
        }
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var reviewer = CreateAdminContractClient(app, 200, AuthRoles.StudentAffairsAdmin);
        Assert.Equal(HttpStatusCode.OK,
            (await manager.PostAsJsonAsync("/api/finance/proposals/1/manager-review", new { review = " Manager note " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await reviewer.PostAsJsonAsync("/api/finance/proposals/2/review", new { review = " Reviewer note " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.PostAsJsonAsync("/api/finance/proposals/3/manager-review", new { review = "Bypass" })).StatusCode);
        await using var verify = app.Services.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var first = await verifyDb.BudgetProposals.SingleAsync(x => x.Id == 1);
        Assert.Equal(FinanceStatuses.Submitted, first.Status);
        Assert.Equal("Manager note", first.ManagerReviewNote);
        var second = await verifyDb.BudgetProposals.SingleAsync(x => x.Id == 2);
        Assert.Equal(FinanceStatuses.ManagerApproved, second.Status);
        Assert.Equal("Reviewer note", second.ReviewNote);
        Assert.Null((await verifyDb.BudgetProposals.SingleAsync(x => x.Id == 3)).ManagerReviewNote);
        Assert.Empty(await verifyDb.FinanceTransactions.ToListAsync());
    }
}
