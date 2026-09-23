using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ClubReportHub.Shared.Auth;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class ClubDataIntegrityConcurrencyTests
{
    private const string SigningKey = "club-data-integrity-test-signing-key-32chars-min";

    [Fact]
    public void ClubDbContextModel_Metadata_VerifiesFilteredUniqueIndexesAndCheckConstraints()
    {
        var options = new DbContextOptionsBuilder<ClubDbContext>()
            .UseSqlServer("Server=fake;Database=fake;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var db = new ClubDbContext(options);
        var designTimeModel = db.GetService<IDesignTimeModel>().Model;

        // DATA-F05: ClubManagerAssignment filtered unique indexes
        var managerEntity = designTimeModel.FindEntityType(typeof(ClubManagerAssignment));
        Assert.NotNull(managerEntity);

        var clubIdIndex = managerEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ClubManagerAssignment.ClubId) && i.IsUnique);
        Assert.NotNull(clubIdIndex);
        Assert.Equal("[IsActive] = 1", clubIdIndex.GetFilter());

        var managerUserIdIndex = managerEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ClubManagerAssignment.ManagerUserId) && i.IsUnique);
        Assert.NotNull(managerUserIdIndex);
        Assert.Equal("[IsActive] = 1", managerUserIdIndex.GetFilter());

        // DATA-F06: ClubDisbandRequest filtered unique index for pending status
        var disbandEntity = designTimeModel.FindEntityType(typeof(ClubDisbandRequest));
        Assert.NotNull(disbandEntity);

        var disbandClubIndex = disbandEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ClubDisbandRequest.ClubId) && i.IsUnique);
        Assert.NotNull(disbandClubIndex);
        Assert.Equal("[Status] = 'Pending'", disbandClubIndex.GetFilter());

        // DATA-F06: ClubOwnershipTransfer filtered unique index for pending status
        var transferEntity = designTimeModel.FindEntityType(typeof(ClubOwnershipTransfer));
        Assert.NotNull(transferEntity);

        var transferClubIndex = transferEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ClubOwnershipTransfer.ClubId) && i.IsUnique);
        Assert.NotNull(transferClubIndex);
        Assert.Equal("[Status] = 'Pending'", transferClubIndex.GetFilter());

        // DATA-F01: ClubMembership filtered unique index and check constraint for TreasurerSlot
        var membershipEntity = designTimeModel.FindEntityType(typeof(ClubMembership));
        Assert.NotNull(membershipEntity);

        var slotIndex = membershipEntity.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 2 &&
                                 i.Properties.Any(p => p.Name == nameof(ClubMembership.ClubId)) &&
                                 i.Properties.Any(p => p.Name == nameof(ClubMembership.TreasurerSlot)) &&
                                 i.IsUnique);
        Assert.NotNull(slotIndex);
        Assert.Equal("[TreasurerSlot] IS NOT NULL", slotIndex.GetFilter());

        var checkConstraint = membershipEntity.GetCheckConstraints()
            .FirstOrDefault(c => c.Name == "CK_ClubMemberships_TreasurerSlot");
        Assert.NotNull(checkConstraint);
        Assert.Equal("[TreasurerSlot] IS NULL OR [TreasurerSlot] IN (1, 2)", checkConstraint.Sql);

        // Concurrency token on Club
        var clubEntity = designTimeModel.FindEntityType(typeof(Club));
        Assert.NotNull(clubEntity);
        var concurrencyProp = clubEntity.FindProperty(nameof(Club.ConcurrencyToken));
        Assert.NotNull(concurrencyProp);
        Assert.True(concurrencyProp.IsConcurrencyToken);
    }

    [Fact]
    public async Task AssignTreasurer_AssignsSlotsSequential_EnforcesMaxTwoTreasurers_ReturnsConflict409()
    {
        await using var app = await CreateTestAppAsync();
        var (clubId, ownerId, member1Id, member2Id, member3Id) = await SeedClubWithMembersAsync(app);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        // Assign member 1 -> should get slot 1
        var res1 = await client.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member1Id, "Member One"));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Assign member 2 -> should get slot 2
        var res2 = await client.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member2Id, "Member Two"));
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // Verify slots in DB
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var m1 = await db.ClubMemberships.FirstAsync(m => m.ClubId == clubId && m.UserId == member1Id);
            var m2 = await db.ClubMemberships.FirstAsync(m => m.ClubId == clubId && m.UserId == member2Id);

            Assert.Equal(ClubMemberRoles.Treasurer, m1.Role);
            Assert.Equal(1, m1.TreasurerSlot);
            Assert.Equal(ClubMemberRoles.Treasurer, m2.Role);
            Assert.Equal(2, m2.TreasurerSlot);
        }

        // Attempt to assign member 3 -> should fail with 409 Conflict (max 2 exceeded)
        var res3 = await client.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member3Id, "Member Three"));
        Assert.Equal(HttpStatusCode.Conflict, res3.StatusCode);
        var err = await res3.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("at most two treasurers", err?.Message ?? string.Empty);
    }

    [Fact]
    public async Task AssignTreasurer_WhenTreasurerRemoved_FreesSlotForNewTreasurer()
    {
        await using var app = await CreateTestAppAsync();
        var (clubId, ownerId, member1Id, member2Id, member3Id) = await SeedClubWithMembersAsync(app);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        // Assign member 1 and member 2
        await client.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member1Id, "Member One"));
        await client.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member2Id, "Member Two"));

        int member1MembershipId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            member1MembershipId = (await db.ClubMemberships.FirstAsync(m => m.ClubId == clubId && m.UserId == member1Id)).Id;
        }

        // Remove treasurer role from member 1
        var removeRes = await client.PostAsync($"/api/clubs/memberships/{member1MembershipId}/member", null);
        Assert.Equal(HttpStatusCode.OK, removeRes.StatusCode);

        // Verify slot 1 is now free in DB
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var m1 = await db.ClubMemberships.FirstAsync(m => m.Id == member1MembershipId);
            Assert.Equal(ClubMemberRoles.Member, m1.Role);
            Assert.Null(m1.TreasurerSlot);
        }

        // Assign member 3 -> should succeed and acquire free slot 1!
        var res3 = await client.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member3Id, "Member Three"));
        Assert.Equal(HttpStatusCode.OK, res3.StatusCode);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var m3 = await db.ClubMemberships.FirstAsync(m => m.ClubId == clubId && m.UserId == member3Id);
            Assert.Equal(ClubMemberRoles.Treasurer, m3.Role);
            Assert.Equal(1, m3.TreasurerSlot);
        }
    }

    [Fact]
    public async Task AssignTreasurer_ConcurrentAssignments_AllowsMaxTwoAndNormalizesConflict409()
    {
        await using var app = await CreateTestAppAsync();
        var (clubId, ownerId, member1Id, member2Id, member3Id) = await SeedClubWithMembersAsync(app);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        // Assign 1st treasurer
        var first = await client.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member1Id, "Member One"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Now run concurrent assignments for member 2 and member 3 (only 1 slot remaining!)
        using var clientA = app.GetTestClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        using var clientB = app.GetTestClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        var taskA = clientA.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member2Id, "Member Two"));
        var taskB = clientB.PostAsJsonAsync($"/api/clubs/{clubId}/treasurers", new AssignTreasurerRequest(member3Id, "Member Three"));

        var responses = await Task.WhenAll(taskA, taskB);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        // Verify DB only has 2 treasurers total
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var treasurers = await db.ClubMemberships
                .Where(m => m.ClubId == clubId && m.Role == ClubMemberRoles.Treasurer && m.Status == ClubMembershipStatuses.Approved)
                .ToListAsync();

            Assert.Equal(2, treasurers.Count);
            Assert.Contains(treasurers, t => t.TreasurerSlot == 1);
            Assert.Contains(treasurers, t => t.TreasurerSlot == 2);
        }
    }

    [Fact]
    public async Task AssignManager_WhenManagerAlreadyManagesAnotherClub_ReturnsConflict409()
    {
        await using var app = await CreateTestAppAsync();
        int clubAId, clubBId;
        const int managerUserId = 999;

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            await db.Database.EnsureCreatedAsync();

            var clubA = CreateClub("CLUB-A", "Club A");
            clubA.ManagerAssignments.Add(new ClubManagerAssignment
            {
                ManagerUserId = managerUserId,
                ManagerName = "Existing Manager",
                IsActive = true
            });

            var clubB = CreateClub("CLUB-B", "Club B");
            db.Clubs.AddRange(clubA, clubB);
            await db.SaveChangesAsync();

            clubAId = clubA.Id;
            clubBId = clubB.Id;
        }

        using var adminClient = app.GetTestClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(1, AuthRoles.Admin));

        // Attempt to assign managerUserId to Club B as well
        var response = await adminClient.PostAsJsonAsync(
            $"/api/clubs/{clubBId}/managers",
            new AssignManagerRequest(managerUserId, "Existing Manager"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("manage one club only", err?.Message ?? string.Empty);
    }

    [Fact]
    public async Task AssignManager_ConcurrentAssignments_AllowsOnlyOneActiveManagerPerClub()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"clubhub-manager-concurrency-{Guid.NewGuid():N}.db");
        var app = await CreateTestAppAsync(options => options.UseSqlite($"Data Source={databasePath};Default Timeout=30"));
        try
        {
            int clubId;

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
                await db.Database.EnsureCreatedAsync();

                var club = CreateClub("CLUB-M", "Club M");
                db.Clubs.Add(club);
                await db.SaveChangesAsync();
                clubId = club.Id;
            }

            using var clientA = app.GetTestClient();
            clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(1, AuthRoles.Admin));

            using var clientB = app.GetTestClient();
            clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(1, AuthRoles.Admin));

            var taskA = clientA.PostAsJsonAsync($"/api/clubs/{clubId}/managers", new AssignManagerRequest(801, "Manager 801"));
            var taskB = clientB.PostAsJsonAsync($"/api/clubs/{clubId}/managers", new AssignManagerRequest(802, "Manager 802"));

            var responses = await Task.WhenAll(taskA, taskB);

            var successResponses = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
            var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

            Assert.NotEmpty(successResponses);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
                var activeManagers = await db.ClubManagerAssignments
                    .Where(m => m.ClubId == clubId && m.IsActive)
                    .ToListAsync();

                Assert.Single(activeManagers);
            }
        }
        finally
        {
            await app.DisposeAsync();
            if (File.Exists(databasePath))
            {
                try { File.Delete(databasePath); } catch { }
            }
        }
    }

    [Fact]
    public async Task SubmitDisbandRequest_WhenPendingRequestExists_ReturnsConflict409()
    {
        await using var app = await CreateTestAppAsync();
        var (clubId, ownerId, _, _, _) = await SeedClubWithMembersAsync(app);

        using var ownerClient = app.GetTestClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        // Submit first request -> 201 Created
        var res1 = await ownerClient.PostAsJsonAsync($"/api/clubs/{clubId}/disband", new DisbandClubRequest("Lack of members"));
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Submit second request -> 409 Conflict
        var res2 = await ownerClient.PostAsJsonAsync($"/api/clubs/{clubId}/disband", new DisbandClubRequest("Duplicate request"));
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        var err = await res2.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("pending disband request already exists", err?.Message ?? string.Empty);
    }

    [Fact]
    public async Task SubmitDisbandRequest_ConcurrentRequests_OnlyOneSucceedsSecondReturnsConflict409()
    {
        await using var app = await CreateTestAppAsync();
        var (clubId, ownerId, _, _, _) = await SeedClubWithMembersAsync(app);

        using var clientA = app.GetTestClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        using var clientB = app.GetTestClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        var taskA = clientA.PostAsJsonAsync($"/api/clubs/{clubId}/disband", new DisbandClubRequest("Concurrent reason A"));
        var taskB = clientB.PostAsJsonAsync($"/api/clubs/{clubId}/disband", new DisbandClubRequest("Concurrent reason B"));

        var responses = await Task.WhenAll(taskA, taskB);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, createdCount);
        Assert.Equal(1, conflictCount);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var pendingCount = await db.ClubDisbandRequests.CountAsync(x => x.ClubId == clubId && x.Status == ClubDisbandStatuses.Pending);
            Assert.Equal(1, pendingCount);
        }
    }

    [Fact]
    public async Task SubmitTransferRequest_WhenPendingRequestExists_ReturnsConflict409()
    {
        await using var app = await CreateTestAppAsync();
        var (clubId, ownerId, member1Id, member2Id, _) = await SeedClubWithMembersAsync(app);

        using var ownerClient = app.GetTestClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        // Submit first request -> 201 Created
        var res1 = await ownerClient.PostAsJsonAsync($"/api/clubs/{clubId}/transfer-ownership", new TransferOwnershipRequest(member1Id, "Graduating"));
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Submit second request -> 409 Conflict
        var res2 = await ownerClient.PostAsJsonAsync($"/api/clubs/{clubId}/transfer-ownership", new TransferOwnershipRequest(member2Id, "Duplicate"));
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
        var err = await res2.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("pending transfer request already exists", err?.Message ?? string.Empty);
    }

    [Fact]
    public async Task SubmitTransferRequest_ConcurrentRequests_OnlyOneSucceedsSecondReturnsConflict409()
    {
        await using var app = await CreateTestAppAsync();
        var (clubId, ownerId, member1Id, member2Id, _) = await SeedClubWithMembersAsync(app);

        using var clientA = app.GetTestClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        using var clientB = app.GetTestClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(ownerId, AuthRoles.ClubManager));

        var taskA = clientA.PostAsJsonAsync($"/api/clubs/{clubId}/transfer-ownership", new TransferOwnershipRequest(member1Id, "Transfer A"));
        var taskB = clientB.PostAsJsonAsync($"/api/clubs/{clubId}/transfer-ownership", new TransferOwnershipRequest(member2Id, "Transfer B"));

        var responses = await Task.WhenAll(taskA, taskB);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, createdCount);
        Assert.Equal(1, conflictCount);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var pendingCount = await db.ClubOwnershipTransfers.CountAsync(x => x.ClubId == clubId && x.Status == ClubOwnershipTransferStatuses.Pending);
            Assert.Equal(1, pendingCount);
        }
    }

    [Fact]
    public async Task ApproveTransferRequest_WhenNewOwnerManagesAnotherClub_ReturnsConflict409()
    {
        await using var app = await CreateTestAppAsync();
        int clubAId, clubBId, transferRequestId;
        const int ownerAId = 501;
        const int managerBId = 502; // Manages Club B, but is also a member in Club A

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            await db.Database.EnsureCreatedAsync();

            var clubA = CreateClub("CLUB-ALPHA", "Club Alpha");
            clubA.ManagerAssignments.Add(new ClubManagerAssignment { ManagerUserId = ownerAId, ManagerName = "Owner Alpha", IsActive = true });
            clubA.Memberships.Add(new ClubMembership { UserId = managerBId, FullName = "Manager Beta", Status = ClubMembershipStatuses.Approved });

            var clubB = CreateClub("CLUB-BETA", "Club Beta");
            clubB.ManagerAssignments.Add(new ClubManagerAssignment { ManagerUserId = managerBId, ManagerName = "Manager Beta", IsActive = true });

            db.Clubs.AddRange(clubA, clubB);
            await db.SaveChangesAsync();

            clubAId = clubA.Id;
            clubBId = clubB.Id;

            var transferRequest = new ClubOwnershipTransfer
            {
                ClubId = clubAId,
                CurrentOwnerUserId = ownerAId,
                CurrentOwnerName = "Owner Alpha",
                NewOwnerUserId = managerBId,
                NewOwnerName = "Manager Beta",
                Reason = "Passing ownership",
                Status = ClubOwnershipTransferStatuses.Pending,
                RequestedAtUtc = DateTimeOffset.UtcNow
            };
            db.ClubOwnershipTransfers.Add(transferRequest);
            await db.SaveChangesAsync();
            transferRequestId = transferRequest.Id;
        }

        using var adminClient = app.GetTestClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(1, AuthRoles.Admin));

        var response = await adminClient.PostAsJsonAsync(
            $"/api/clubs/transfer-requests/{transferRequestId}/approve",
            new ApproveTransferRequest("Approved by student affairs"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Contains("already an active manager of another club", err?.Message ?? string.Empty);
    }

    // ============================================================================
    // Helper Methods & Setup
    // ============================================================================

    private static async Task<WebApplication> CreateTestAppAsync(Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"club-integrity-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ClubDbContext>(options =>
        {
            if (configureDatabase is null)
            {
                options.UseInMemoryDatabase(dbName);
            }
            else
            {
                configureDatabase(options);
            }
        });
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMembershipEndpoints();
        app.MapManagerEndpoints();
        app.MapDisbandEndpoints();
        app.MapTransferEndpoints();

        await app.StartAsync();
        return app;
    }

    private static async Task<(int ClubId, int OwnerId, int Member1Id, int Member2Id, int Member3Id)> SeedClubWithMembersAsync(WebApplication app)
    {
        const int ownerId = 100;
        const int m1 = 201;
        const int m2 = 202;
        const int m3 = 203;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        await db.Database.EnsureCreatedAsync();

        var club = CreateClub($"CLUB-{Guid.NewGuid():N}"[..10], "Integrity Test Club");
        club.ManagerAssignments.Add(new ClubManagerAssignment
        {
            ManagerUserId = ownerId,
            ManagerName = "Club Owner",
            IsActive = true
        });

        club.Memberships.Add(new ClubMembership { UserId = m1, FullName = "Member One", Email = "m1@test.com", Status = ClubMembershipStatuses.Approved });
        club.Memberships.Add(new ClubMembership { UserId = m2, FullName = "Member Two", Email = "m2@test.com", Status = ClubMembershipStatuses.Approved });
        club.Memberships.Add(new ClubMembership { UserId = m3, FullName = "Member Three", Email = "m3@test.com", Status = ClubMembershipStatuses.Approved });

        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        return (club.Id, ownerId, m1, m2, m3);
    }

    private static Club CreateClub(string code, string name) => new()
    {
        Code = code,
        Name = name,
        Category = ClubCategories.Academic,
        Description = name,
        ContactEmail = $"{code.ToLowerInvariant()}@example.edu",
        ContactPhone = "0123456789",
        IsActive = true
    };

    private static string CreateToken(int userId, string role)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "ClubReportHub",
            audience: "ClubReportHub.Client",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record ErrorResponse(string? Message);
}
