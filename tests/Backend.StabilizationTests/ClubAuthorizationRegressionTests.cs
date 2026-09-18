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
