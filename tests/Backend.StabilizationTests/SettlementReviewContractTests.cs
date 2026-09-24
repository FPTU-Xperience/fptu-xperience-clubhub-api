using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ClubReportHub.Shared.Auth;
using FinanceService.Data;
using FinanceService.Endpoints;
using FinanceService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class SettlementReviewContractTests
{
    private const string SigningKey = "SettlementReviewContractSigningKey1234567890!@#$";

    [Fact]
    public async Task SettlementSubmitOnlyAcknowledgesExistingSubmittedState()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var manager = fixture.Client(101, AuthRoles.ClubManager);
        var response = await manager.PostAsync("/api/finance/settlements/1/submit", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("pending_review", body.GetProperty("status").GetString());
        await using var db = fixture.Db();
        Assert.Equal(FinanceStatuses.Submitted, (await db.Settlements.SingleAsync()).Status);
        Assert.Empty(await db.FinanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task SettlementReviewOnlyAddsNoteWithoutApprovalOrLedger()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var reviewer = fixture.Client(200, AuthRoles.Admin);
        using var manager = fixture.Client(101, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync("/api/finance/settlements/1/review", new { review = "Bypass" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await reviewer.PostAsJsonAsync("/api/finance/settlements/1/review", new { review = " Receipt checked " })).StatusCode);
        await using var db = fixture.Db();
        var settlement = await db.Settlements.SingleAsync();
        Assert.Equal(FinanceStatuses.Submitted, settlement.Status);
        Assert.Equal("Receipt checked", settlement.ReviewNote);
        Assert.Equal(FinanceStatuses.Approved, (await db.BudgetProposals.SingleAsync()).Status);
        Assert.Empty(await db.FinanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task ManualAdjustmentRequiresReviewerReasonAndIdempotency()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var reviewer = fixture.Client(200, AuthRoles.Admin);
        using var manager = fixture.Client(101, AuthRoles.ClubManager);
        var request = new
        {
            clubId = 1,
            type = "adjustment",
            amount = -25m,
            description = "Correction of duplicated expense",
            reason = "Receipt 42 was entered twice",
            idempotencyKey = "manual-adjustment-2026-001"
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync("/api/finance/transactions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await reviewer.PostAsJsonAsync("/api/finance/transactions",
                new
                {
                    clubId = 1,
                    type = TransactionTypes.BudgetApproved,
                    amount = 25m,
                    description = "Forged approval",
                    reason = "Invalid",
                    idempotencyKey = "forged-ledger-001"
                })).StatusCode);
        var first = await reviewer.PostAsJsonAsync("/api/finance/transactions", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await reviewer.PostAsJsonAsync("/api/finance/transactions", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await reviewer.PostAsJsonAsync("/api/finance/transactions",
                new
                {
                    request.clubId,
                    request.type,
                    amount = -30m,
                    request.description,
                    request.reason,
                    request.idempotencyKey
                })).StatusCode);
        await using var db = fixture.Db();
        var transaction = Assert.Single(await db.FinanceTransactions.ToListAsync());
        Assert.Equal(TransactionTypes.ManualAdjustment, transaction.Type);
        Assert.Equal(-25m, transaction.Amount);
        var audit = Assert.Single(await db.ManualFinanceAdjustments.ToListAsync());
        Assert.Equal(transaction.Id, audit.FinanceTransactionId);
        Assert.Equal(200, audit.CreatedByUserId);
        Assert.Equal("Receipt 42 was entered twice", audit.Reason);
        Assert.Equal("manual-adjustment-2026-001", audit.IdempotencyKey);
    }

    [Theory]
    [InlineData(AuthRoles.Admin, 200, HttpStatusCode.OK)]
    [InlineData(AuthRoles.StudentAffairsAdmin, 201, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Admin, 101, HttpStatusCode.BadRequest)]
    [InlineData(AuthRoles.SystemAdmin, 202, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubManager, 203, HttpStatusCode.Forbidden)]
    public async Task SettlementReview_RejectRequiresReviewerAndNotProposalCreator(string role, int actor, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Client(actor, role);
        var response = await client.PostAsJsonAsync("/api/finance/settlements/1/reject", new { note = " Not approved " });
        Assert.Equal(expected, response.StatusCode);
        await using var db = fixture.Db();
        Assert.Equal(expected == HttpStatusCode.OK ? FinanceStatuses.Rejected : FinanceStatuses.Submitted,
            (await db.Settlements.SingleAsync()).Status);
        Assert.Equal(FinanceStatuses.Approved, (await db.BudgetProposals.SingleAsync()).Status);
        Assert.Empty(await db.FinanceTransactions.Where(x => x.Type == TransactionTypes.SettlementApproved).ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SettlementReview_RejectRequiresReason(string? note)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Client(200, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/finance/settlements/1/reject", new { note })).StatusCode);
        await using var db = fixture.Db();
        Assert.Equal(FinanceStatuses.Submitted, (await db.Settlements.SingleAsync()).Status);
    }

    [Fact]
    public async Task SettlementReview_RejectEnforcesNoteLengthAndDoesNotMutate()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Client(200, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/finance/settlements/1/reject", new { note = new string('x', 1001) })).StatusCode);
        await using var db = fixture.Db();
        Assert.Equal(FinanceStatuses.Submitted, (await db.Settlements.SingleAsync()).Status);
    }

    [Fact]
    public async Task SettlementReview_RejectKeepsBudgetApprovedAndAllowsNewSettlement()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var reviewer = fixture.Client(200, AuthRoles.Admin);
        var first = await reviewer.PostAsJsonAsync("/api/finance/settlements/1/reject", new { note = " Missing receipt " });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await reviewer.PostAsJsonAsync("/api/finance/settlements/1/reject", new { note = "Repeat" })).StatusCode);
        using var manager = fixture.Client(101, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.OK,
            (await manager.PostAsJsonAsync("/api/finance/proposals/1/settlements",
                new { totalSpent = 90m, receiptUrl = "https://example.test/replacement.pdf" })).StatusCode);
        await using var db = fixture.Db();
        Assert.Equal(FinanceStatuses.Approved, (await db.BudgetProposals.SingleAsync()).Status);
        Assert.Single(await db.Settlements.Where(x => x.Status == FinanceStatuses.Rejected).ToListAsync());
        Assert.Single(await db.Settlements.Where(x => x.Status == FinanceStatuses.Submitted).ToListAsync());
        Assert.Empty(await db.FinanceTransactions.Where(x => x.Type == TransactionTypes.SettlementApproved).ToListAsync());
    }

    [Fact]
    public async Task SettlementReview_ApprovalIsSingleTransitionWithOneLedgerRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var reviewer = fixture.Client(200, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.OK,
            (await reviewer.PostAsJsonAsync("/api/finance/settlements/1/approve", new { note = "Checked" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await reviewer.PostAsJsonAsync("/api/finance/settlements/1/approve", new { note = "Repeat" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await reviewer.PostAsJsonAsync("/api/finance/settlements/1/reject", new { note = "Too late" })).StatusCode);
        await using var db = fixture.Db();
        Assert.Equal(FinanceStatuses.Settled, (await db.BudgetProposals.SingleAsync()).Status);
        Assert.Equal(2, (await db.BudgetProposals.SingleAsync()).Version);
        Assert.Equal(FinanceStatuses.Approved, (await db.Settlements.SingleAsync()).Status);
        Assert.Single(await db.FinanceTransactions.Where(x => x.Type == TransactionTypes.SettlementApproved).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettlementReview_CompetingDecisionMakesOnlyOneTransitionWin(bool competingApproval)
    {
        var interceptor = new CompetingReviewInterceptor(competingApproval);
        await using var fixture = await Fixture.CreateAsync(interceptor);
        interceptor.Enable(fixture.Connection);
        using var reviewer = fixture.Client(200, AuthRoles.Admin);
        var endpoint = competingApproval ? "reject" : "approve";
        var response = await reviewer.PostAsJsonAsync($"/api/finance/settlements/1/{endpoint}", new { note = "Decision" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = fixture.Db();
        Assert.Equal(competingApproval ? FinanceStatuses.Approved : FinanceStatuses.Rejected,
            (await db.Settlements.SingleAsync()).Status);
        Assert.Equal(competingApproval ? FinanceStatuses.Settled : FinanceStatuses.Approved,
            (await db.BudgetProposals.SingleAsync()).Status);
        Assert.Equal(2, (await db.BudgetProposals.SingleAsync()).Version);
        Assert.Equal(competingApproval ? 1 : 0,
            await db.FinanceTransactions.CountAsync(x => x.Type == TransactionTypes.SettlementApproved));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public SqliteConnection Connection { get; }

        private Fixture(WebApplication app, SqliteConnection connection)
        {
            _app = app;
            Connection = connection;
        }

        public static async Task<Fixture> CreateAsync(SaveChangesInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = SigningKey
            });
            builder.Services.AddDbContext<FinanceDbContext>(options =>
            {
                options.UseSqlite(connection);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddSingleton(CreateClubAccess());
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGroup("/api/finance").RequireAuthorization(AuthPolicies.BusinessAccess).MapSettlementEndpoints();
            app.MapGroup("/api/finance").RequireAuthorization(AuthPolicies.BusinessAccess).MapTransactionEndpoints();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.Settlements.Add(new Settlement
                {
                    Id = 1,
                    BudgetProposal = new BudgetProposal
                    {
                        Id = 1,
                        ClubId = 1,
                        ClubName = "One",
                        Title = "Proposal",
                        Description = "Approved budget",
                        RequestedAmount = 100m,
                        ApprovedAmount = 100m,
                        ProposedByUserId = 101,
                        Status = FinanceStatuses.Approved
                    },
                    TotalSpent = 80m,
                    ReceiptUrl = "https://example.test/original.pdf"
                });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            return new Fixture(app, connection);
        }

        public FinanceDbContext Db() => new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(Connection).Options);

        public HttpClient Client(int actor, string role)
        {
            var client = _app.GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(actor, role));
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            await Connection.DisposeAsync();
        }

        private static ClubAccessClient CreateClubAccess()
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new ClubAccessSnapshot(1, "One", true, true, true, [101], [101], [101]) })
            });
            return new ClubAccessClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5102/") },
                new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<ClubAccessClient>.Instance);
        }
    }

    private sealed class CompetingReviewInterceptor(bool competingApproval) : SaveChangesInterceptor
    {
        private SqliteConnection? _connection;
        private int _used;

        public void Enable(SqliteConnection connection) => _connection = connection;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_connection is null || Interlocked.Exchange(ref _used, 1) != 0)
                return await base.SavingChangesAsync(eventData, result, cancellationToken);

            await using var competing = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(_connection).Options);
            var row = await competing.Settlements.Include(x => x.BudgetProposal).SingleAsync(cancellationToken);
            row.Status = competingApproval ? FinanceStatuses.Approved : FinanceStatuses.Rejected;
            row.ReviewedByUserId = 999;
            row.ReviewedAtUtc = DateTimeOffset.UtcNow;
            row.ReviewNote = "Competing decision";
            row.BudgetProposal.Status = competingApproval ? FinanceStatuses.Settled : FinanceStatuses.Approved;
            row.BudgetProposal.Version++;
            if (competingApproval)
            {
                competing.FinanceTransactions.Add(new FinanceTransaction
                {
                    ClubId = 1,
                    Amount = 80m,
                    Type = TransactionTypes.SettlementApproved,
                    Description = "Competing approval",
                    ReferenceId = 1
                });
            }
            await competing.SaveChangesAsync(cancellationToken);
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static string Token(int userId, string role)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client",
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClaimTypes.Role, role)],
            notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(10), credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }
}
