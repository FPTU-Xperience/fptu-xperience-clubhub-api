using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class ClubAuthorizationRegressionTests
{
    private const string SigningKey = "club-scope-test-signing-key-at-least-32-characters";

    [Fact]
    public async Task ManagerOfClubACannotApproveMembershipForClubB()
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
        var databaseName = $"club-authorization-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ClubDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMembershipEndpoints();

        int foreignMembershipId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            await db.Database.EnsureCreatedAsync();

            var clubA = CreateClub("CLUB-A", "Club A");
            clubA.ManagerAssignments.Add(new ClubManagerAssignment
            {
                ManagerUserId = 101,
                ManagerName = "Manager A",
                IsActive = true
            });
            var clubB = CreateClub("CLUB-B", "Club B");
            var membership = new ClubMembership
            {
                UserId = 202,
                FullName = "Student B",
                Email = "student-b@example.edu",
                Status = ClubMembershipStatuses.Pending
            };
            clubB.Memberships.Add(membership);
            db.Clubs.AddRange(clubA, clubB);
            await db.SaveChangesAsync();
            foreignMembershipId = membership.Id;
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(userId: 101, AuthRoles.ClubManager));

        var response = await client.PostAsJsonAsync(
            $"/api/clubs/memberships/{foreignMembershipId}/approve",
            new { note = "attempted cross-club approval" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verificationScope = app.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var unchanged = await verificationDb.ClubMemberships
            .SingleAsync(item => item.Id == foreignMembershipId);
        Assert.Equal(ClubMembershipStatuses.Pending, unchanged.Status);
        Assert.Null(unchanged.ReviewedByUserId);
    }

    [Fact]
    public async Task InvitedMemberMustPersonallyAcceptBeforeManagerCanApprove()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });
        var databaseName = $"member-invitation-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ClubDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMembershipEndpoints();
        app.MapMemberManagementEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            var club = CreateClub("CLUB-A", "Club A");
            club.Id = 1;
            club.ManagerAssignments.Add(new ClubManagerAssignment { ManagerUserId = 101, IsActive = true });
            db.Clubs.Add(club);
            await db.SaveChangesAsync();
            Assert.True(await db.Clubs.AnyAsync(x => x.Id == 1 && x.IsActive));
        }
        await app.StartAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<ClubDbContext>()
                .Clubs.AnyAsync(x => x.Id == 1 && x.IsActive));
        }
        using var manager = app.GetTestClient();
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(101, AuthRoles.ClubManager));
        using var member = app.GetTestClient();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(202, AuthRoles.ClubMember));

        var invite = await manager.PostAsJsonAsync("/api/clubs/1/members",
            new { userId = "202", fullName = "Proposed name", role = "CLUB_MEMBER" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var invited = await invite.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(ClubMembershipStatuses.Pending, invited.GetProperty("status").GetString());
        var membershipId = invited.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict,
            (await manager.PostAsJsonAsync($"/api/clubs/memberships/{membershipId}/approve", new { note = "premature" })).StatusCode);

        var join = await member.PostAsJsonAsync("/api/clubs/1/join", new
        {
            fullName = "Confirmed name",
            dateOfBirth = "2002-01-01",
            gender = "MALE",
            email = "member@example.edu",
            phoneNumber = "0123456789",
            reason = "I want to join",
            acceptedClubRules = true,
            committedToParticipate = true
        });
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await manager.PostAsJsonAsync($"/api/clubs/memberships/{membershipId}/approve", new { note = "accepted" })).StatusCode);
        await using var verifyScope = app.Services.CreateAsyncScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<ClubDbContext>()
            .ClubMemberships.SingleAsync(x => x.Id == membershipId);
        Assert.Equal(202, stored.UserId);
        Assert.Equal("Confirmed name", stored.FullName);
        Assert.True(stored.AcceptedClubRules);
        Assert.True(stored.CommittedToParticipate);
        Assert.Equal(ClubMembershipStatuses.Approved, stored.Status);
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
}
