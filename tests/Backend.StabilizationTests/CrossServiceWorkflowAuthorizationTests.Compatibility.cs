using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using FinanceService.Clients;
using FinanceService.Data;
using FinanceService.Endpoints;
using FinanceService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Theory]
    [InlineData("", 1, 20, 20)]
    [InlineData("?page=2", 2, 20, 5)]
    [InlineData("?pageSize=10", 1, 10, 10)]
    [InlineData("?page=0&pageSize=0", 1, 20, 20)]
    [InlineData("?page=-1&pageSize=200", 1, 100, 25)]
    [InlineData("?page=2147483647&pageSize=100", 2147483647, 100, 0)]
    public async Task CompatibilityContract_FinancePaginationIsOptionalAndBounded(
        string query, int page, int pageSize, int count)
    {
        await using var app = await CreateCompatibilityAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.StudentAffairsAdmin);
        foreach (var resource in new[] { "proposals", "settlements" })
        {
            var response = await client.GetAsync($"/api/finance/{resource}{query}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(25, body.GetProperty("total").GetInt32());
            Assert.Equal(page, body.GetProperty("page").GetInt32());
            Assert.Equal(pageSize, body.GetProperty("pageSize").GetInt32());
            Assert.Equal(count, body.GetProperty("items").GetArrayLength());
        }
    }

    [Theory]
    [InlineData(AuthRoles.ClubManager, 100, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Treasurer, 100, HttpStatusCode.OK)]
    [InlineData(AuthRoles.ClubMember, 100, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.SystemAdmin, 100, HttpStatusCode.Forbidden)]
    public async Task CompatibilityContract_FinancePaginationPreservesScope(string role, int actor, HttpStatusCode expected)
    {
        var canManage = role == AuthRoles.ClubManager;
        var isTreasurer = role == AuthRoles.Treasurer;
        await using var app = await CreateCompatibilityAppAsync(canManage, isTreasurer);
        using var client = CreateAdminContractClient(app, actor, role);
        foreach (var resource in new[] { "proposals", "settlements" })
        {
            var response = await client.GetAsync($"/api/finance/{resource}");
            Assert.Equal(expected, response.StatusCode);
            if (expected == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(24, body.GetProperty("total").GetInt32());
                Assert.DoesNotContain(body.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt32() == 25);
            }
        }
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/finance/proposals?clubId=2")).StatusCode);
        }
    }

    [Theory]
    [InlineData(AuthRoles.ClubManager, 100, HttpStatusCode.OK)]
    [InlineData(AuthRoles.ClubManager, 101, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubManager, 102, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Treasurer, 103, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.ClubMember, 104, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Admin, 105, HttpStatusCode.OK)]
    [InlineData(AuthRoles.StudentAffairsAdmin, 106, HttpStatusCode.OK)]
    [InlineData(AuthRoles.SystemAdmin, 107, HttpStatusCode.Forbidden)]
    public async Task CompatibilityContract_MemberProfileRequiresManagementOfExactClub(
        string role, int actor, HttpStatusCode expected)
    {
        await using var app = await CreateCompatibilityAppAsync();
        using var client = CreateAdminContractClient(app, actor, role);
        var response = await client.PutAsJsonAsync("/api/clubs/1/members/10", new
        {
            fullName = " Updated name ",
            email = " updated@example.test ",
            phoneNumber = " 0123456789 ",
            address = " New address "
        });
        Assert.Equal(expected, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var member = await scope.ServiceProvider.GetRequiredService<ClubDbContext>().ClubMemberships.SingleAsync(x => x.Id == 10);
        Assert.Equal(expected == HttpStatusCode.OK ? "Updated name" : "Original name", member.FullName);
        Assert.Equal(1, member.ClubId);
        Assert.Equal(104, member.UserId);
        Assert.Equal(ClubMemberRoles.Treasurer, member.Role);
        Assert.Equal(1, member.TreasurerSlot);
        Assert.Equal(ClubMembershipStatuses.Approved, member.Status);
        Assert.True(member.AcceptedClubRules);
        Assert.True(member.CommittedToParticipate);
        Assert.Equal("Application reason", member.Reason);
        Assert.Equal(new DateOnly(2000, 1, 1), member.DateOfBirth);
        if (expected == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(10, body.GetProperty("id").GetInt32());
            Assert.Equal("updated@example.test", body.GetProperty("email").GetString());
            Assert.Equal("0123456789", member.PhoneNumber);
            Assert.Equal("New address", member.Address);
        }
    }

    [Theory]
    [InlineData("{\"role\":\"TREASURER\"}")]
    [InlineData("{\"userId\":999,\"fullName\":\"Forged\"}")]
    [InlineData("{\"clubId\":2,\"fullName\":\"Forged\"}")]
    [InlineData("{\"status\":\"Approved\"}")]
    [InlineData("{\"acceptedClubRules\":false}")]
    [InlineData("{\"isDeleted\":false}")]
    [InlineData("{\"fullName\":\"   \"}")]
    [InlineData("{\"email\":\"not-an-email\"}")]
    [InlineData("{\"phoneNumber\":\"   \"}")]
    [InlineData("{\"dateOfBirth\":\"9999-12-31\"}")]
    [InlineData("{\"fullName\":null}")]
    [InlineData("{}")]
    public async Task CompatibilityContract_MemberProfileRejectsInvalidOrProtectedFields(string json)
    {
        await using var app = await CreateCompatibilityAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/clubs/1/members/10", content)).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var member = await scope.ServiceProvider.GetRequiredService<ClubDbContext>().ClubMemberships.SingleAsync(x => x.Id == 10);
        Assert.Equal("Original name", member.FullName);
        Assert.Equal(ClubMemberRoles.Treasurer, member.Role);
        Assert.Equal(104, member.UserId);
        Assert.True(member.AcceptedClubRules);
    }

    [Theory]
    [InlineData("fullName", 201)]
    [InlineData("email", 256)]
    [InlineData("phoneNumber", 41)]
    [InlineData("address", 501)]
    [InlineData("gender", 21)]
    public async Task CompatibilityContract_MemberProfileValidatesDatabaseLengths(string field, int length)
    {
        await using var app = await CreateCompatibilityAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/clubs/1/members/10", new Dictionary<string, string> { [field] = new('x', length) })).StatusCode);
    }

    [Fact]
    public async Task CompatibilityContract_MemberProfileKeepsUnspecifiedFieldsAndDeletedMembersHidden()
    {
        await using var app = await CreateCompatibilityAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.Admin);
        var response = await client.PutAsJsonAsync("/api/clubs/1/members/10", new { address = "", gender = "Other", dateOfBirth = "2001-02-03" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Original name", body.GetProperty("fullName").GetString());
        Assert.Equal("2001-02-03", body.GetProperty("dateOfBirth").GetString());
        Assert.Equal("", body.GetProperty("address").GetString());
        foreach (var path in new[] { "/api/clubs/2/members/10", "/api/clubs/1/members/11", "/api/clubs/1/members/999" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(path, new { fullName = "Changed" })).StatusCode);
        }
        using var anonymous = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/api/clubs/1/members/10", new { fullName = "Changed" })).StatusCode);
    }

    private static async Task<WebApplication> CreateCompatibilityAppAsync(bool manager = true, bool treasurer = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });
        var dbName = $"compatibility-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ClubDbContext>(options => options.UseInMemoryDatabase(dbName + "-club"));
        builder.Services.AddDbContext<FinanceDbContext>(options => options.UseInMemoryDatabase(dbName + "-finance"));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(CreateClubAccessClient(new ClubAccessSnapshot(1, "Club one", manager, treasurer, true, [100], [100], treasurer ? [100] : [])));
        builder.Services.AddSingleton(CreateActivityCatalogClient());
        builder.Services.AddSingleton(new FutureEventReportClient(
            CreateFakeHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound), "http://localhost:5103/"),
            NullLogger<FutureEventReportClient>.Instance));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberManagementEndpoints();
        var finance = app.MapGroup("/api/finance").RequireAuthorization(AuthPolicies.BusinessAccess);
        finance.MapProposalEndpoints();
        finance.MapSettlementEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            db.Clubs.AddRange(new Club { Id = 1, Code = "ONE", Name = "Club one" }, new Club { Id = 2, Code = "TWO", Name = "Club two" });
            db.ClubManagerAssignments.AddRange(
                new ClubManagerAssignment { ClubId = 1, ManagerUserId = 100, IsActive = true },
                new ClubManagerAssignment { ClubId = 2, ManagerUserId = 101, IsActive = true },
                new ClubManagerAssignment { ClubId = 1, ManagerUserId = 102, IsActive = false });
            db.ClubMemberships.AddRange(
                new ClubMembership
                {
                    Id = 10,
                    ClubId = 1,
                    UserId = 104,
                    FullName = "Original name",
                    Email = "original@example.test",
                    PhoneNumber = "0900000000",
                    Role = ClubMemberRoles.Treasurer,
                    TreasurerSlot = 1,
                    Status = ClubMembershipStatuses.Approved,
                    AcceptedClubRules = true,
                    CommittedToParticipate = true,
                    Reason = "Application reason",
                    DateOfBirth = new DateOnly(2000, 1, 1)
                },
                new ClubMembership { Id = 11, ClubId = 1, UserId = 105, IsDeleted = true });
            await db.SaveChangesAsync();
            var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            for (var id = 1; id <= 25; id++)
            {
                financeDb.Settlements.Add(new Settlement
                {
                    Id = id,
                    BudgetProposal = new BudgetProposal
                    {
                        Id = id,
                        ClubId = id == 25 ? 2 : 1,
                        Title = $"Proposal {id}",
                        Status = FinanceStatuses.Approved,
                        ProposedAtUtc = DateTimeOffset.UtcNow.AddDays(-id)
                    },
                    TotalSpent = 100m,
                    SubmittedAtUtc = DateTimeOffset.UtcNow.AddDays(-id)
                });
            }
            await financeDb.SaveChangesAsync();
        }
        await app.StartAsync();
        return app;
    }
}
