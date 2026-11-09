using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AdminService.Data;
using AdminService.Endpoints;
using AdminService.Security;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubReportHub.Shared.Rosters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class ClubCreditTests
{
    [Fact]
    public async Task CreditUsesOnlyOwnVerifiedClubLedgerAndNeverSampleMoneyOrExternalPoints()
    {
        await using var f = await Fixture.Create(); using var member = f.Client(200);
        var wallet = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me?userId=201");
        Assert.Equal(200, wallet.GetProperty("userId").GetInt32()); Assert.Equal(100m, wallet.GetProperty("balance").GetDecimal());
        Assert.Equal("CLUB_CREDIT", wallet.GetProperty("unit").GetString());
        Assert.True(wallet.GetProperty("experiencePointsAreNotSpent").GetBoolean());
        await f.Db(async db =>
        {
            var effect = await db.ExperienceLedgerEffects.SingleAsync(x => x.ContributionId == f.ClubContribution);
            db.AddRange(new ExperienceLedgerAllocation { EffectId = effect.Id, Pillar = "ACAD", Amount = 60 },
                new ExperienceLedgerAllocation { EffectId = effect.Id, Pillar = "COMM", Amount = 40 });
            await db.SaveChangesAsync();
        });
        var alias = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/points/me");
        Assert.Equal(wallet.GetProperty("version").GetGuid(), alias.GetProperty("version").GetGuid());
        Assert.Equal(200m, (await member.GetFromJsonAsync<JsonElement>("/api/clubs/2/wallet/me")).GetProperty("balance").GetDecimal());
        using var empty = f.Client(201);
        Assert.Equal(0m, (await empty.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me")).GetProperty("balance").GetDecimal());
        var ledger = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/points/me/ledger?page=1&pageSize=1");
        Assert.Equal(1, ledger.GetProperty("totalItems").GetInt32());
        var graph = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me/graph?days=7");
        Assert.Equal(7, graph.GetProperty("points").GetArrayLength()); Assert.Equal(100m, graph.GetProperty("points")[6].GetProperty("balance").GetDecimal());
        Assert.Equal("Asia/Ho_Chi_Minh", graph.GetProperty("timezone").GetString());
    }

    [Fact]
    public async Task RedeemIsVersionedIdempotentAndKeepsXpAndOtherClubBalancesUntouched()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client(100); using var member = f.Client(200);
        var reward = await f.Reward(owner, 80); var body = await f.RedeemBody(member, reward);
        var url = $"/api/clubs/1/rewards/{reward.GetProperty("id").GetGuid()}/redeem";
        var first = await Send(member, url, body, "stable-redemption"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var receipt = await first.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(20m, receipt.GetProperty("balance").GetDecimal());
        var replay = await Json(await Send(member, url, body, "stable-redemption"));
        Assert.Equal(receipt.GetProperty("receiptId").GetGuid(), replay.GetProperty("receiptId").GetGuid());
        var retry = await Json(await Send(member, url, body, "other-key"));
        Assert.Equal(receipt.GetProperty("receiptId").GetGuid(), retry.GetProperty("receiptId").GetGuid());
        Assert.Equal(20m, (await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me")).GetProperty("balance").GetDecimal());
        Assert.Equal(200m, (await member.GetFromJsonAsync<JsonElement>("/api/clubs/2/wallet/me")).GetProperty("balance").GetDecimal());
        var inventory = await member.GetFromJsonAsync<JsonElement>("/api/users/me/cosmetics"); Assert.Equal(1, inventory.GetProperty("totalItems").GetInt32());
        await f.Db(async db => Assert.Equal(100m, await db.ExperienceLedgerEffects.Where(x => x.ContributionId == f.ClubContribution).Select(x => x.Amount).SingleAsync()));
        var receipts = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/rewards/redemptions/me"); Assert.Equal(1, receipts.GetProperty("totalItems").GetInt32());
        using var other = f.Client(201); Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/users/me/cosmetics?userId=200")).GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task ReversalCreatesDebtBlocksEvenFreeClaimsAndRetainsPriorOwnership()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client(100); using var member = f.Client(200);
        var reward = await f.Reward(owner, 80);
        Assert.Equal(HttpStatusCode.OK, (await Send(member, $"/api/clubs/1/rewards/{reward.GetProperty("id").GetGuid()}/redeem", await f.RedeemBody(member, reward), "spend")).StatusCode);
        await f.Delta(-100);
        var wallet = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me"); Assert.Equal(-80m, wallet.GetProperty("balance").GetDecimal()); Assert.True(wallet.GetProperty("hasDebt").GetBoolean());
        var free = await f.Reward(owner, 0);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(member, $"/api/clubs/1/rewards/{free.GetProperty("id").GetGuid()}/redeem", await f.RedeemBody(member, free), "free-after-reversal")).StatusCode);
        Assert.Equal(1, (await member.GetFromJsonAsync<JsonElement>("/api/users/me/cosmetics")).GetProperty("totalItems").GetInt32());
        var graph = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me/graph?days=1"); Assert.Equal(-80m, graph.GetProperty("points")[0].GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task PublicationTimeAndCurrentMembershipAreEnforcedAndPriceCannotBeSuppliedOnRedemption()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client(100); using var member = f.Client(200);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(member, "/api/clubs/1/rewards", Draft(1), "not-manager")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, "/api/clubs/2/rewards", Draft(1), "other-club")).StatusCode);
        var reward = await f.Reward(owner, 10, f.Clock.Utc.AddMinutes(1), f.Clock.Utc.AddMinutes(2));
        var url = $"/api/clubs/1/rewards/{reward.GetProperty("id").GetGuid()}/redeem";
        Assert.Equal(HttpStatusCode.Conflict, (await Send(member, url, await f.RedeemBody(member, reward), "not-open")).StatusCode);
        f.Clock.Utc = f.Clock.Utc.AddMinutes(1);
        var wallet = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(member, url, new { expectedRewardVersion = reward.GetProperty("version").GetGuid(), expectedWalletVersion = wallet.GetProperty("version").GetGuid(), price = 0 }, "spoof-price")).StatusCode);
        f.Authority.MemberActive = false;
        var catalog = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/rewards");
        Assert.False(catalog.GetProperty("capabilities").GetProperty("canRedeem").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(member, url, await f.RedeemBody(member, reward), "lost-membership")).StatusCode);
        f.Authority.MemberActive = true; f.Clock.Utc = f.Clock.Utc.AddMinutes(1);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(member, url, await f.RedeemBody(member, reward), "closed-boundary")).StatusCode);
        var edited = await Send(owner, $"/api/clubs/1/rewards/{reward.GetProperty("id").GetGuid()}", Draft(0, reward.GetProperty("version").GetGuid()), "edit-published", HttpMethod.Patch);
        Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
    }

    [Fact]
    public async Task StaleWalletCannotSpendNewCreditAndLedgerRecordsAreAppendOnly()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client(100); using var member = f.Client(200);
        var reward = await f.Reward(owner, 80); var before = await f.RedeemBody(member, reward); await f.Delta(20);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(member, $"/api/clubs/1/rewards/{reward.GetProperty("id").GetGuid()}/redeem", before, "stale")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(member, $"/api/clubs/1/rewards/{reward.GetProperty("id").GetGuid()}/redeem", await f.RedeemBody(member, reward), "fresh")).StatusCode);
        await f.Db(async db => { var row = await db.Set<ClubRewardRedemption>().SingleAsync(); row.Amount = 0; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); });
    }

    [Fact]
    public async Task BundleChargesPublishedFullPriceOnceGrantsOnlyMissingComponentsAndRetirementKeepsOwnership()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client(100); using var member = f.Client(200);
        var color = await f.Reward(owner, 40);
        await Json(await Send(member, $"/api/clubs/1/rewards/{color.GetProperty("id").GetGuid()}/redeem", await f.RedeemBody(member, color), "own-color"));
        var shapeDraft = await Json(await Send(owner, "/api/clubs/1/rewards", new { label = "Club stamp", description = "Verified PNG", kind = "SHAPE", price = 30, assetUrl = "https://assets.example.test/verified.png" }, "shape-draft"));
        var shape = await Json(await Send(owner, $"/api/clubs/1/rewards/{shapeDraft.GetProperty("id").GetGuid()}/publish", new { expectedVersion = shapeDraft.GetProperty("version").GetGuid() }, "shape-publish"));
        var ids = new[] { color.GetProperty("grants")[0].GetProperty("cosmeticId").GetGuid(), shape.GetProperty("grants")[0].GetProperty("cosmeticId").GetGuid() };
        var draft = await Json(await Send(owner, "/api/clubs/1/rewards", new { label = "Palette and stamp", description = "Bundle", kind = "BUNDLE", price = 70, grantIds = ids }, "bundle-draft"));
        var bundle = await Json(await Send(owner, $"/api/clubs/1/rewards/{draft.GetProperty("id").GetGuid()}/publish", new { expectedVersion = draft.GetProperty("version").GetGuid() }, "bundle-publish"));
        await f.Delta(20);
        var receipt = await Json(await Send(member, $"/api/clubs/1/rewards/{bundle.GetProperty("id").GetGuid()}/redeem", await f.RedeemBody(member, bundle), "bundle-redeem"));
        Assert.Equal(70m, receipt.GetProperty("amount").GetDecimal()); Assert.Equal(10m, receipt.GetProperty("balance").GetDecimal());
        await f.Db(async db => { Assert.Equal(2, await db.Set<ClubCosmeticOwnership>().CountAsync()); Assert.Equal(110m, (await db.Set<ClubCreditWallet>().SingleAsync()).Spent); });
        await Json(await Send(owner, $"/api/clubs/1/rewards/{color.GetProperty("id").GetGuid()}/retire", new { expectedVersion = color.GetProperty("version").GetGuid(), reason = "End of campaign" }, "retire-color"));
        Assert.Equal(2, (await member.GetFromJsonAsync<JsonElement>("/api/users/me/cosmetics")).GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task SqlUpgradePreservesPriorVerifiedXpAndCreatesNoSampleWalletOrEntitlements()
    {
        await using var f = await Fixture.Create(sql: true); using var member = f.Client(200);
        var wallet = await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me");
        Assert.Equal(100m, wallet.GetProperty("balance").GetDecimal());
        await f.Db(async db => { Assert.Empty(await db.Set<ClubCreditWallet>().ToListAsync()); Assert.Empty(await db.Set<ClubCosmeticOwnership>().ToListAsync()); Assert.Equal(3, await db.ExperienceLedgerEffects.CountAsync()); });
    }

    [Fact]
    public async Task SqlConcurrentDifferentRewardsCannotOverspendOneWallet()
    {
        await using var f = await Fixture.Create(sql: true); using var owner = f.Client(100); using var member = f.Client(200);
        var first = await f.Reward(owner, 80); var second = await f.Reward(owner, 80);
        var body1 = await f.RedeemBody(member, first); var body2 = await f.RedeemBody(member, second);
        var results = await Task.WhenAll(Send(member, $"/api/clubs/1/rewards/{first.GetProperty("id").GetGuid()}/redeem", body1, "parallel-1"),
            Send(member, $"/api/clubs/1/rewards/{second.GetProperty("id").GetGuid()}/redeem", body2, "parallel-2"));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await f.Db(async db => { Assert.Equal(80m, (await db.Set<ClubCreditWallet>().SingleAsync()).Spent); Assert.Single(await db.Set<ClubRewardRedemption>().ToListAsync()); });
    }

    private static object Draft(decimal price, Guid? version = null) => new
    {
        label = "Club palette",
        description = "Verified reward",
        kind = "COLOR",
        price,
        palette = new { background = "#112233", secondary = "#445566", accent = "#778899", ink = "#FFFFFF" },
        expectedVersion = version
    };
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Fixture HTTP {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
    private static Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string key, HttpMethod? method = null)
    { var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }
    private sealed class Clock : TimeProvider { internal DateTimeOffset Utc = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Authority : HttpMessageHandler
    {
        internal bool MemberActive = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("accounts/resolve"))
            {
                var ids = (await request.Content!.ReadFromJsonAsync<SchoolAccountQuery>(ct))!.UserIds!;
                return Ok(ids.Select(id => new SchoolAccount(id, $"student{id}@fpt.edu.vn", "Student " + id, "HAN", true, false, true, true)));
            }
            if (path.EndsWith("/access"))
            {
                var id = int.Parse(new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Subject);
                return Ok(new[] { new ClubAccessSnapshot(1, "Club 1", id == 100, false, id == 100 || MemberActive && id == 200, [100]) });
            }
            if (path.EndsWith("/media/verify"))
            {
                var input = await request.Content!.ReadFromJsonAsync<AuthService.Endpoints.VerifyMediaRequest>(ct);
                return input is { Kind: "club-reward", OwnerId: 1, PublicUrl: "https://assets.example.test/verified.png" }
                    ? Ok(new { publicUrl = input.PublicUrl }) : new(HttpStatusCode.Forbidden);
            }
            var clubId = int.Parse(path.Split('/').Last());
            var member = request.RequestUri.Query.Contains("userId=200", StringComparison.Ordinal) && MemberActive && clubId == 1;
            return Ok(new ClubExperienceAuthority(clubId, "Club " + clubId, "HAN", true, member, null));
        }
        private static HttpResponseMessage Ok(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
    private sealed class Fixture(WebApplication app, SqliteConnection? sqlite, Authority authority, Clock clock) : IAsyncDisposable
    {
        internal Authority Authority => authority; internal Clock Clock => clock; internal Guid ClubContribution;
        private const string Key = "test-club-credit-only-signing-key-over-32-characters";
        internal static async Task<Fixture> Create(bool sql = false)
        {
            var sqlite = sql ? null : new SqliteConnection("Data Source=:memory:"); if (sqlite is not null) await sqlite.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = Key, ["Security:InternalWorkflowToken"] = "test-credit-internal" });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddAuthorization(options => options.AddPolicy(AdminPolicies.AnyActor, policy => policy.RequireAuthenticatedUser()));
            var connection = sql ? new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING")
                ?? "Server=(localdb)\\ClubManagerApiTests;Integrated Security=True;TrustServerCertificate=True")
            { InitialCatalog = "ClubCreditTests_" + Guid.NewGuid().ToString("N") }.ConnectionString : null;
            builder.Services.AddDbContext<AdminDbContext>(options => { if (sql) options.UseSqlServer(connection, x => x.EnableRetryOnFailure()); else options.UseSqlite(sqlite!); });
            var authority = new Authority(); var client = new HttpClient(authority) { BaseAddress = new Uri("http://authority/") }; var clock = new Clock();
            builder.Services.AddSingleton<TimeProvider>(clock); builder.Services.AddSingleton(new SchoolAccountClient(client, builder.Configuration));
            builder.Services.AddSingleton(new ClubExperienceAuthorityClient(client, builder.Configuration));
            builder.Services.AddSingleton(new ClubAccessClient(client, new MemoryCache(new MemoryCacheOptions()), NullLogger<ClubAccessClient>.Instance));
            builder.Services.AddSingleton(new MediaAttachmentClient(client, builder.Configuration));
            var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapClubCreditEndpoints();
            var fixture = new Fixture(app, sqlite, authority, clock);
            await fixture.Db(async db =>
            {
                if (sql) await db.GetService<IMigrator>().MigrateAsync("20261022143753_AcademicCalendarBySemester"); else await db.Database.EnsureCreatedAsync();
                foreach (var item in new[] { (Club: 1, Type: "CLUB", Amount: 100m), (Club: 2, Type: "CLUB", Amount: 200m), (Club: 1, Type: "EXTERNAL", Amount: 999m) })
                {
                    var row = new VerifiedContribution { UserId = 200, ClubId = item.Club, SourceType = item.Type, SourceKey = Guid.NewGuid().ToString(), SemesterId = "FALL2026", Status = "APPROVED", RawPoints = item.Amount, CreatedAtUtc = clock.Utc, UpdatedAtUtc = clock.Utc, OccurredAt = clock.Utc };
                    if (item is { Club: 1, Type: "CLUB" }) fixture.ClubContribution = row.Id;
                    db.Add(row); db.Add(new ExperienceLedgerEffect { ContributionId = row.Id, RevisionId = Guid.NewGuid(), UserId = 200, SemesterId = "FALL2026", EffectType = "AWARD", Amount = item.Amount, VerificationId = Guid.NewGuid(), PolicyVersion = Guid.NewGuid(), FormulaVersion = "VERIFIED_TEST_POLICY", RecordedAt = clock.Utc, OccurredAt = clock.Utc });
                }
                await db.SaveChangesAsync();
                if (sql) await db.Database.MigrateAsync();
            }); await app.StartAsync(); return fixture;
        }
        internal async Task Db(Func<AdminDbContext, Task> action) { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AdminDbContext>()); }
        internal Task Delta(decimal amount) => Db(async db => { db.Add(new ExperienceLedgerEffect { ContributionId = ClubContribution, RevisionId = Guid.NewGuid(), UserId = 200, SemesterId = "FALL2026", EffectType = amount < 0 ? "REVOKE" : "ADJUST", Amount = amount, VerificationId = Guid.NewGuid(), PolicyVersion = Guid.NewGuid(), FormulaVersion = "VERIFIED_TEST_POLICY", RecordedAt = clock.Utc, OccurredAt = clock.Utc }); await db.SaveChangesAsync(); });
        internal HttpClient Client(int id)
        {
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client", [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.Role, id == 100 ? AuthRoles.ClubManager : AuthRoles.ClubMember), new Claim("campus", "HAN")], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task<JsonElement> Reward(HttpClient owner, decimal price, DateTimeOffset? opens = null, DateTimeOffset? closes = null)
        {
            var draft = await Json(await Send(owner, "/api/clubs/1/rewards", Draft(price), Guid.NewGuid().ToString()));
            return await Json(await Send(owner, $"/api/clubs/1/rewards/{draft.GetProperty("id").GetGuid()}/publish", new { expectedVersion = draft.GetProperty("version").GetGuid(), opensAt = opens, closesAt = closes }, Guid.NewGuid().ToString()));
        }
        internal async Task<object> RedeemBody(HttpClient member, JsonElement reward) => new { expectedRewardVersion = reward.GetProperty("version").GetGuid(), expectedWalletVersion = (await member.GetFromJsonAsync<JsonElement>("/api/clubs/1/wallet/me")).GetProperty("version").GetGuid() };
        public async ValueTask DisposeAsync() { if (sqlite is null) await Db(async db => { await db.Database.EnsureDeletedAsync(); }); await app.DisposeAsync(); if (sqlite is not null) await sqlite.DisposeAsync(); }
    }
}
