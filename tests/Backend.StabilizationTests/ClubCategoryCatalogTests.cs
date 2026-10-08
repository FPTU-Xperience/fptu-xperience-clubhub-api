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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class ClubCategoryCatalogTests
{
    private const string SigningKey = "club-scope-test-signing-key-at-least-32-characters";

    [Fact]
    public async Task GetCategories_ReturnsDefaultCategoriesAndCounts()
    {
        var (app, client) = await CreateTestAppAsync();
        await using (app)
        {
            var adminToken = CreateToken(1, AuthRoles.StudentAffairsAdmin);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var response = await client.GetAsync("/api/clubs/categories");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var categories = await response.Content.ReadFromJsonAsync<IReadOnlyList<ClubCategoryResponse>>();
            Assert.NotNull(categories);
            Assert.NotEmpty(categories);
            Assert.Contains(categories, c => c.Code == "TECHNOLOGY" && c.Name == "Công nghệ");
            Assert.Contains(categories, c => c.Code == "ARTS" && c.Name == "Nghệ thuật");
        }
    }

    [Fact]
    public async Task CreateCategory_AdminCanCreate_AndDuplicateReturnsConflict()
    {
        var (app, client) = await CreateTestAppAsync();
        await using (app)
        {
            var adminToken = CreateToken(1, AuthRoles.StudentAffairsAdmin);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var createReq = new CreateClubCategoryRequest(
                Name: "Esports & Gaming",
                Code: "ESPORTS",
                Description: "Câu lạc bộ thể thao điện tử");

            var createRes = await client.PostAsJsonAsync("/api/clubs/categories", createReq);
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

            var created = await createRes.Content.ReadFromJsonAsync<ClubCategoryResponse>();
            Assert.NotNull(created);
            Assert.Equal("ESPORTS", created.Code);
            Assert.Equal("Esports & Gaming", created.Name);

            // Duplicate should conflict
            var duplicateRes = await client.PostAsJsonAsync("/api/clubs/categories", createReq);
            Assert.Equal(HttpStatusCode.Conflict, duplicateRes.StatusCode);
        }
    }

    [Fact]
    public async Task CreateClub_WithCustomCategory_ResolvesCategoryCorrectly()
    {
        var (app, client) = await CreateTestAppAsync();
        await using (app)
        {
            var adminToken = CreateToken(1, AuthRoles.StudentAffairsAdmin);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // Create custom category
            var catRes = await client.PostAsJsonAsync("/api/clubs/categories", new CreateClubCategoryRequest("Robotics", "ROBOTICS"));
            Assert.Equal(HttpStatusCode.Created, catRes.StatusCode);

            // Create club with that category
            var clubReq = new CreateClubRequest(
                Code: "ROBO_CLUB",
                Name: "Robotics Club",
                Category: "Robotics",
                Description: "CLB nghiên cứu chế tạo Robot",
                ContactEmail: "robotics@fpt.edu.vn",
                ContactPhone: "0987654321");

            var clubRes = await client.PostAsJsonAsync("/api/clubs", clubReq);
            Assert.Equal(HttpStatusCode.Created, clubRes.StatusCode);

            var createdClub = await clubRes.Content.ReadFromJsonAsync<ClubResponse>();
            Assert.NotNull(createdClub);
            Assert.Equal("ROBOTICS", createdClub.Category);

            // Verify count in categories endpoint
            var listRes = await client.GetAsync("/api/clubs/categories");
            var list = await listRes.Content.ReadFromJsonAsync<IReadOnlyList<ClubCategoryResponse>>();
            Assert.NotNull(list);
            var roboCat = list.FirstOrDefault(c => c.Code == "ROBOTICS");
            Assert.NotNull(roboCat);
            Assert.Equal(1, roboCat.ClubCount);
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestAppAsync()
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
        var databaseName = $"club-categories-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ClubDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapClubEndpoints();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

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
                new Claim(ClaimTypes.Role, role),
                new Claim("user_id", userId.ToString()),
                new Claim("campus", CampusCodes.Hanoi)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
