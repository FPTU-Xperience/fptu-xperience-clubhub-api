using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public Task OwnerRequestAtomicity_DisbandInsertFailureDoesNotAdvanceClubVersion() =>
        AssertOwnerRequestInsertIsAtomic(transfer: false);

    [Fact]
    public Task OwnerRequestAtomicity_TransferInsertFailureDoesNotAdvanceClubVersion() =>
        AssertOwnerRequestInsertIsAtomic(transfer: true);

    private static async Task AssertOwnerRequestInsertIsAtomic(bool transfer)
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var failure = new OwnerRequestInsertFailure(transfer);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });
        builder.Services.AddDbContext<ClubDbContext>(options => options.UseSqlServer(database.ConnectionString).AddInterceptors(failure));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDisbandEndpoints();
        app.MapTransferEndpoints();
        Guid originalVersion;
        int clubId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            await db.Database.MigrateAsync();
            var club = new Club { Code = "ATOMIC", Name = "Atomic owner requests", IsActive = true };
            db.Clubs.Add(club);
            await db.SaveChangesAsync();
            clubId = club.Id;
            originalVersion = club.ConcurrencyToken;
            db.ClubManagerAssignments.Add(new ClubManagerAssignment
            {
                ClubId = clubId,
                ManagerUserId = 100,
                ManagerName = "Owner",
                IsActive = true
            });
            db.ClubMemberships.Add(new ClubMembership
            {
                ClubId = clubId,
                UserId = 104,
                FullName = "Eligible new owner",
                Status = ClubMembershipStatuses.Approved,
                AcceptedClubRules = true,
                CommittedToParticipate = true
            });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        failure.Enabled = true;
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var response = transfer
            ? await manager.PostAsJsonAsync($"/api/clubs/{clubId}/transfer-ownership", new { newOwnerUserId = 104, reason = "Transfer request atomicity regression" })
            : await manager.PostAsJsonAsync($"/api/clubs/{clubId}/disband", new { reason = "Disband request atomicity regression" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, failure.InjectedFailures);
        await using var verify = app.Services.CreateAsyncScope();
        var saved = verify.ServiceProvider.GetRequiredService<ClubDbContext>();
        var current = await saved.Clubs.AsNoTracking().SingleAsync(x => x.Id == clubId);
        Assert.Equal(originalVersion, current.ConcurrencyToken);
        Assert.True(current.IsActive);
        Assert.Empty(await saved.ClubDisbandRequests.AsNoTracking().ToArrayAsync());
        Assert.Empty(await saved.ClubOwnershipTransfers.AsNoTracking().ToArrayAsync());
        var owner = await saved.ClubManagerAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(100, owner.ManagerUserId);
        Assert.True(owner.IsActive);
    }

    private sealed class OwnerRequestInsertFailure(bool transfer) : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        internal int InjectedFailures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context is { } db && (transfer
                ? db.ChangeTracker.Entries<ClubOwnershipTransfer>().Any(x => x.State == EntityState.Added)
                : db.ChangeTracker.Entries<ClubDisbandRequest>().Any(x => x.State == EntityState.Added)))
            {
                InjectedFailures++;
                throw new DbUpdateException("Injected owner request insertion failure.");
            }
            return new(result);
        }
    }
}
