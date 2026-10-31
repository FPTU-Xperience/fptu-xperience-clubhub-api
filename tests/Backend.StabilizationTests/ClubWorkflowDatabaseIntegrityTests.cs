using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class ClubWorkflowDatabaseIntegrityTests
{
    private const string SigningKey = "club-workflow-integrity-test-signing-key-at-least-32";

    [Fact]
    public async Task Migration_DuplicateLegacyPendingApplicationsFailWithoutDataLossAndCanRetryAfterReconciliation()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var options = new DbContextOptionsBuilder<ClubDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ClubDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261022100406_InvitationRosterAuditContext");
        var historyBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        foreach (var code in new[] { "LEGACY-A", "LEGACY-B" })
        {
            // The original required columns are explicit; later legacy migrations supply
            // defaults for the expanded form. The pre-upgrade table has no request token.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [ClubCreationApplications]
                    ([RequesterUserId], [RequesterName], [Code], [Name], [Description],
                     [ContactEmail], [ContactPhone], [Status], [SubmittedAtUtc])
                VALUES (501, N'Legacy founder', {code}, N'Legacy application', N'Preserve old form data',
                        N'legacy@fpt.edu.vn', N'0912345678', N'Submitted', {DateTimeOffset.UtcNow})
                """);
        }

        var failure = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync());
        Assert.Equal(51031, failure.Number);
        Assert.Equal(historyBefore, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [ClubCreationApplications] WHERE [RequesterUserId] = 501 AND [Status] = N'Submitted'").SingleAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [ClubCreationApplications]").SingleAsync());

        // An explicit operator decision reconciles one application; migration itself must
        // never invent a rejection or delete historical applications to satisfy uniqueness.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE [ClubCreationApplications]
            SET [Status] = N'NeedsRevision', [ReviewNote] = N'Explicitly reconciled before schema upgrade'
            WHERE [Code] = N'LEGACY-B'
            """);
        await migrator.MigrateAsync();
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), id => id.EndsWith("_ProtectClubRequestReviews", StringComparison.Ordinal));
        var applications = await db.ClubCreationApplications.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync();
        Assert.Equal(2, applications.Length);
        Assert.Equal(ClubApplicationStatuses.Submitted, applications[0].Status);
        Assert.Equal(ClubApplicationStatuses.NeedsRevision, applications[1].Status);
        Assert.Equal("Explicitly reconciled before schema upgrade", applications[1].ReviewNote);
        Assert.All(applications, application => Assert.NotEqual(Guid.Empty, application.ConcurrencyToken));
        Assert.Equal(2, applications.Select(application => application.ConcurrencyToken).Distinct().Count());
        db.ClubCreationApplications.Add(new ClubCreationApplication
        {
            RequesterUserId = 501,
            Code = "LEGACY-C",
            Name = "Duplicate after upgrade",
            Status = ClubApplicationStatuses.Submitted
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sql = Assert.IsType<SqlException>(duplicate.InnerException);
        Assert.Contains(sql.Number, new[] { 2601, 2627 });
        Assert.Equal(2, await db.ClubCreationApplications.CountAsync());
    }

    [Theory]
    [InlineData("creation", true)]
    [InlineData("creation", false)]
    [InlineData("disband", true)]
    [InlineData("disband", false)]
    [InlineData("transfer", true)]
    [InlineData("transfer", false)]
    public async Task Review_StaleDecisionReturns409AndCannotOverwriteWinnerOrItsSideEffects(string workflow, bool approveWins)
    {
        var gate = new ReviewGate(workflow, blockApproval: !approveWins);
        await using var fixture = await WorkflowApp.CreateAsync(gate);
        using var staleClient = fixture.Client(1, AuthRoles.Admin);
        using var winnerClient = fixture.Client(2, AuthRoles.Admin);
        var root = workflow switch
        {
            "creation" => $"/api/clubs/applications/{fixture.CreationId}",
            "disband" => $"/api/clubs/disband-requests/{fixture.DisbandId}",
            _ => $"/api/clubs/transfer-requests/{fixture.TransferId}"
        };
        gate.Enabled = true;
        var staleTask = staleClient.PostAsJsonAsync(root + (approveWins ? "/reject" : "/approve"),
            new { note = "Stale reviewer decision", adminNote = "Stale reviewer decision" });
        HttpResponseMessage staleResponse;
        try
        {
            await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var winner = await winnerClient.PostAsJsonAsync(root + (approveWins ? "/approve" : "/reject"),
                new { note = "Winning reviewer decision", adminNote = "Winning reviewer decision" });
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        }
        finally
        {
            gate.Release.TrySetResult(true);
        }
        staleResponse = await staleTask;
        using (staleResponse)
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        if (workflow == "creation")
        {
            var application = await db.ClubCreationApplications.AsNoTracking().SingleAsync(x => x.Id == fixture.CreationId);
            Assert.Equal(approveWins ? ClubApplicationStatuses.Approved : ClubApplicationStatuses.Rejected, application.Status);
            Assert.Equal(2, application.ReviewedByUserId);
            Assert.Equal(approveWins ? 2 : 1, await db.Clubs.CountAsync());
            Assert.Equal(approveWins ? 1 : 0, await db.ClubManagerAssignments.CountAsync(x => x.ManagerUserId == 301 && x.IsActive));
            Assert.Equal(approveWins ? 1 : 0, await db.ClubMemberships.CountAsync(x => x.UserId == 301));
            Assert.Equal(approveWins, application.CreatedClubId.HasValue);
            Assert.Equal(approveWins ? 1 : 0, await db.OutboxMessages.CountAsync(x => x.EventType != RecommendationChangeCapture.Route));
        }
        else if (workflow == "disband")
        {
            var request = await db.ClubDisbandRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.DisbandId);
            var club = await db.Clubs.AsNoTracking().SingleAsync(x => x.Id == fixture.ClubId);
            Assert.Equal(approveWins ? ClubDisbandStatuses.Approved : ClubDisbandStatuses.Rejected, request.Status);
            Assert.Equal(2, request.ReviewedByUserId);
            Assert.Equal(!approveWins, club.IsActive);
            Assert.Equal(approveWins, club.DeletedAtUtc.HasValue);
            Assert.Equal(approveWins, request.ExecutedAtUtc.HasValue);
            Assert.Empty(await db.OutboxMessages.Where(x => x.EventType != RecommendationChangeCapture.Route).ToArrayAsync());
        }
        else
        {
            var request = await db.ClubOwnershipTransfers.AsNoTracking().SingleAsync(x => x.Id == fixture.TransferId);
            Assert.Equal(approveWins ? ClubOwnershipTransferStatuses.Approved : ClubOwnershipTransferStatuses.Rejected, request.Status);
            Assert.Equal(2, request.ReviewedByUserId);
            var owner = await db.ClubManagerAssignments.AsNoTracking().SingleAsync(x => x.ClubId == fixture.ClubId && x.IsActive);
            Assert.Equal(approveWins ? 202 : 100, owner.ManagerUserId);
            Assert.Equal(approveWins, request.ExecutedAtUtc.HasValue);
            Assert.Equal(approveWins ? 1 : 0, await db.OutboxMessages.CountAsync(x => x.EventType != RecommendationChangeCapture.Route));
        }
        // One catalog invalidation comes from fixture setup. Only an approved
        // winning transition adds one more; the stale reviewer must add none.
        Assert.Equal(approveWins ? 2 : 1, await db.OutboxMessages.CountAsync(x => x.EventType == RecommendationChangeCapture.Route));
    }

    [Fact]
    public async Task Submit_ConcurrentApplicationsForOneStudentCreateOnePendingAndReturn409()
    {
        var gate = new SubmissionGate();
        await using var fixture = await WorkflowApp.CreateAsync(gate);
        using var first = fixture.Client(401, AuthRoles.ClubMember);
        using var second = fixture.Client(401, AuthRoles.ClubMember);
        gate.Enabled = true;
        var results = await Task.WhenAll(
            first.PostAsJsonAsync("/api/clubs/applications", Application("SUBMIT-A")),
            second.PostAsJsonAsync("/api/clubs/applications", Application("SUBMIT-B")));
        try
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(2, gate.Arrivals);
        }
        finally
        {
            foreach (var response in results) response.Dispose();
        }
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var pending = await db.ClubCreationApplications.AsNoTracking()
            .Where(x => x.RequesterUserId == 401 && x.Status == ClubApplicationStatuses.Submitted).ToArrayAsync();
        Assert.Single(pending);
        Assert.Empty(await db.Clubs.Where(x => x.Code == "SUBMIT-A" || x.Code == "SUBMIT-B").ToArrayAsync());
        Assert.Empty(await db.OutboxMessages.Where(x => x.EventType != RecommendationChangeCapture.Route).ToArrayAsync());
        Assert.Single(await db.OutboxMessages.Where(x => x.EventType == RecommendationChangeCapture.Route).ToArrayAsync());
    }

    [Fact]
    public async Task Resubmit_WhenAnotherApplicationIsPendingPreservesRevisionRequestAndReturns409()
    {
        await using var fixture = await WorkflowApp.CreateAsync();
        Guid originalToken;
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var first = await db.ClubCreationApplications.SingleAsync(x => x.Id == fixture.CreationId);
            first.Status = ClubApplicationStatuses.NeedsRevision;
            first.ReviewNote = "Clarify membership plan";
            await db.SaveChangesAsync();
            originalToken = first.ConcurrencyToken;
            db.ClubCreationApplications.Add(new ClubCreationApplication
            {
                RequesterUserId = 301,
                Code = "OTHER",
                Name = "Other application",
                Status = ClubApplicationStatuses.Submitted
            });
            await db.SaveChangesAsync();
        }
        using var student = fixture.Client(301, AuthRoles.ClubMember);
        using var result = await student.PutAsJsonAsync($"/api/clubs/applications/{fixture.CreationId}", Application("NEWCLUB"));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        await using var verify = fixture.App.Services.CreateAsyncScope();
        var saved = verify.ServiceProvider.GetRequiredService<ClubDbContext>();
        var application = await saved.ClubCreationApplications.AsNoTracking().SingleAsync(x => x.Id == fixture.CreationId);
        Assert.Equal(ClubApplicationStatuses.NeedsRevision, application.Status);
        Assert.Equal("Clarify membership plan", application.ReviewNote);
        Assert.Equal(originalToken, application.ConcurrencyToken);
        Assert.Equal(1, await saved.ClubCreationApplications.CountAsync(x => x.RequesterUserId == 301 && x.Status == ClubApplicationStatuses.Submitted));
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("sync-keep-state")]
    [InlineData("async")]
    [InlineData("async-keep-state")]
    public async Task SaveOverloads_RotateAllRequestTokensAndRejectStaleWrites(string overload)
    {
        await using var fixture = await WorkflowApp.CreateAsync();
        var options = new DbContextOptionsBuilder<ClubDbContext>().UseSqlServer(fixture.Database.ConnectionString).Options;
        await using var stale = new ClubDbContext(options);
        var oldApplication = await stale.ClubCreationApplications.SingleAsync();
        var oldDisband = await stale.ClubDisbandRequests.SingleAsync();
        var oldTransfer = await stale.ClubOwnershipTransfers.SingleAsync();
        var oldMembership = await stale.ClubMemberships.SingleAsync();
        var original = new[] { oldApplication.ConcurrencyToken, oldDisband.ConcurrencyToken, oldTransfer.ConcurrencyToken, oldMembership.ConcurrencyToken };
        await using (var writer = new ClubDbContext(options))
        {
            (await writer.ClubCreationApplications.SingleAsync()).ReviewNote = "Updated";
            (await writer.ClubDisbandRequests.SingleAsync()).AdminNote = "Updated";
            (await writer.ClubOwnershipTransfers.SingleAsync()).AdminNote = "Updated";
            (await writer.ClubMemberships.SingleAsync()).FullName = "Updated member";
            switch (overload)
            {
                case "sync": writer.SaveChanges(); break;
                case "sync-keep-state": writer.SaveChanges(false); break;
                case "async": await writer.SaveChangesAsync(); break;
                default: await writer.SaveChangesAsync(false); break;
            }
        }
        await using (var verify = new ClubDbContext(options))
        {
            var current = new[]
            {
                (await verify.ClubCreationApplications.SingleAsync()).ConcurrencyToken,
                (await verify.ClubDisbandRequests.SingleAsync()).ConcurrencyToken,
                (await verify.ClubOwnershipTransfers.SingleAsync()).ConcurrencyToken,
                (await verify.ClubMemberships.SingleAsync()).ConcurrencyToken
            };
            Assert.All(current, token => Assert.NotEqual(Guid.Empty, token));
            for (var index = 0; index < current.Length; index++) Assert.NotEqual(original[index], current[index]);
        }
        oldApplication.ReviewNote = "Stale note";
        oldDisband.AdminNote = "Stale note";
        oldTransfer.AdminNote = "Stale note";
        oldMembership.FullName = "Stale member";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var saved = new ClubDbContext(options);
        Assert.Equal("Updated", (await saved.ClubCreationApplications.SingleAsync()).ReviewNote);
        Assert.Equal("Updated", (await saved.ClubDisbandRequests.SingleAsync()).AdminNote);
        Assert.Equal("Updated", (await saved.ClubOwnershipTransfers.SingleAsync()).AdminNote);
        Assert.Equal("Updated member", (await saved.ClubMemberships.SingleAsync()).FullName);
    }

    private static CreateClubApplicationRequest Application(string code) => new(
        code, "Database integrity club", ClubCategories.Academic, "Study together", "Club application for SQL integrity tests", null,
        "Student founder", "Student", "founder@fpt.edu.vn", "0912345678", "FPT University", 1, [], true,
        "Weekly study sessions", "Weekly", null, null, null, ClubResourceOptions.SelfManaged, ClubFundingOptions.SelfFunded,
        null, false, true, true, true);

    private sealed class ReviewGate(string workflow, bool blockApproval) : SaveChangesInterceptor
    {
        private int blocked;
        internal bool Enabled { get; set; }
        internal TaskCompletionSource<bool> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context is ClubDbContext db && Matches(db) && Interlocked.CompareExchange(ref blocked, 1, 0) == 0)
            {
                Arrived.TrySetResult(true);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }

        private bool Matches(ClubDbContext db) => workflow switch
        {
            "creation" when blockApproval => db.ChangeTracker.Entries<Club>().Any(x => x.State == EntityState.Added),
            "creation" => db.ChangeTracker.Entries<ClubCreationApplication>().Any(x => x.State == EntityState.Modified && x.Entity.Status == ClubApplicationStatuses.Rejected),
            "disband" => db.ChangeTracker.Entries<ClubDisbandRequest>().Any(x => x.State == EntityState.Modified
                && x.Entity.Status == (blockApproval ? ClubDisbandStatuses.Approved : ClubDisbandStatuses.Rejected)),
            _ => db.ChangeTracker.Entries<ClubOwnershipTransfer>().Any(x => x.State == EntityState.Modified
                && x.Entity.Status == (blockApproval ? ClubOwnershipTransferStatuses.Approved : ClubOwnershipTransferStatuses.Rejected))
        };
    }

    private sealed class SubmissionGate : SaveChangesInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Enabled { get; set; }
        internal int Arrivals => arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context is ClubDbContext db
                && db.ChangeTracker.Entries<ClubCreationApplication>().Any(x => x.State == EntityState.Added && x.Entity.RequesterUserId == 401))
            {
                if (Interlocked.Increment(ref arrivals) == 2) release.TrySetResult(true);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class WorkflowApp(ManagerSqlDatabase database, WebApplication app, int clubId, int creationId, int disbandId, int transferId) : IAsyncDisposable
    {
        internal ManagerSqlDatabase Database { get; } = database;
        internal WebApplication App { get; } = app;
        internal int ClubId { get; } = clubId;
        internal int CreationId { get; } = creationId;
        internal int DisbandId { get; } = disbandId;
        internal int TransferId { get; } = transferId;

        internal static async Task<WorkflowApp> CreateAsync(SaveChangesInterceptor? interceptor = null)
        {
            var database = await ManagerSqlDatabase.CreateAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = SigningKey
            });
            builder.Services.AddDbContext<ClubDbContext>(options =>
            {
                options.UseSqlServer(database.ConnectionString);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            var app = builder.Build();
            try
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapApplicationEndpoints();
                app.MapDisbandEndpoints();
                app.MapTransferEndpoints();
                int clubId, creationId, disbandId, transferId;
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
                    await db.Database.MigrateAsync();
                    var club = new Club { Code = "EXISTING", Name = "Existing club", IsActive = true };
                    club.ManagerAssignments.Add(new ClubManagerAssignment { ManagerUserId = 100, ManagerName = "Current owner", IsActive = true });
                    club.Memberships.Add(new ClubMembership
                    {
                        UserId = 202,
                        FullName = "Eligible new owner",
                        Status = ClubMembershipStatuses.Approved,
                        AcceptedClubRules = true,
                        CommittedToParticipate = true
                    });
                    db.Clubs.Add(club);
                    var application = new ClubCreationApplication
                    {
                        RequesterUserId = 301,
                        RequesterName = "New founder",
                        Code = "NEWCLUB",
                        Name = "New club",
                        ContactEmail = "founder@fpt.edu.vn",
                        Category = ClubCategories.Academic,
                        Status = ClubApplicationStatuses.Submitted
                    };
                    db.ClubCreationApplications.Add(application);
                    await db.SaveChangesAsync();
                    var disband = new ClubDisbandRequest
                    {
                        ClubId = club.Id,
                        RequesterUserId = 100,
                        RequesterName = "Current owner",
                        Status = ClubDisbandStatuses.Pending,
                        Reason = "Club disband request",
                        RequestedAtUtc = DateTimeOffset.UtcNow
                    };
                    var transfer = new ClubOwnershipTransfer
                    {
                        ClubId = club.Id,
                        CurrentOwnerUserId = 100,
                        CurrentOwnerName = "Current owner",
                        NewOwnerUserId = 202,
                        NewOwnerName = "Eligible new owner",
                        Status = ClubOwnershipTransferStatuses.Pending,
                        Reason = "Club transfer request",
                        RequestedAtUtc = DateTimeOffset.UtcNow
                    };
                    db.ClubDisbandRequests.Add(disband); db.ClubOwnershipTransfers.Add(transfer);
                    await db.SaveChangesAsync();
                    clubId = club.Id; creationId = application.Id; disbandId = disband.Id; transferId = transfer.Id;
                }
                await app.StartAsync();
                return new WorkflowApp(database, app, clubId, creationId, disbandId, transferId);
            }
            catch
            {
                await app.DisposeAsync();
                await database.DisposeAsync();
                throw;
            }
        }

        internal HttpClient Client(int userId, string role)
        {
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client",
                [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClaimTypes.Role, role)],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
            var client = App.GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
