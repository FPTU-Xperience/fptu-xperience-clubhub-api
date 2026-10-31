using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Models;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed class RecommendationPipelineTests
{
    private const string Token = "Synthetic-Recommendation-Internal-Token-2026";

    [Fact]
    public async Task AuthExport_IsPrivatePagedAndContainsOnlyFeatureData()
    {
        await using var sql = new SqliteConnection("DataSource=:memory:");
        await sql.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:InternalWorkflowToken"] = Token });
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication("Bearer").AddJwtBearer();
        builder.Services.AddDbContext<AuthDbContext>(x => x.UseSqlite(sql));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRecommendationSourceEndpoints();
        await app.StartAsync();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new User { Id = 11, Username = "u11", Email = "private@example.invalid", FullName = "Private person" },
                new User { Id = 12, Username = "u12", Email = "locked@example.invalid", FullName = "Locked", IsLocked = true });
            db.UserProfiles.Add(new CanonicalUserProfile { UserId = 11, PhoneNumber = "private-phone", Address = "private-address", InterestsJson = "[\"technology\"]" });
            await db.SaveChangesAsync();
        }
        using var client = app.GetTestClient();
        var query = new RecommendationExportQuery(0, 1, []);
        var denied = await client.PostAsJsonAsync("/api/internal/recommendation/students", query);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", Token);
        var response = await client.PostAsJsonAsync("/api/internal/recommendation/students", query);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private@example", body);
        Assert.DoesNotContain("Private person", body);
        Assert.DoesNotContain("private-phone", body);
        Assert.DoesNotContain("private-address", body);
        var page = await response.Content.ReadFromJsonAsync<RecommendationStudentPage>();
        Assert.NotNull(page);
        Assert.Equal(11, page.NextUserId);
        Assert.Single(page.Items);
        Assert.Equal("UNKNOWN", page.Items[0].ScheduleAvailability);
        Assert.Equal("technology", Assert.Single(page.Items[0].Interests));
        response = await client.PostAsJsonAsync("/api/internal/recommendation/students", new RecommendationExportQuery(11, 50, []));
        Assert.Empty((await response.Content.ReadFromJsonAsync<RecommendationStudentPage>())!.Items);
    }

    [Fact]
    public async Task ProfileAndScheduleInvalidation_CommitWithSourceAndRollbackTogether()
    {
        await using var sql = new SqliteConnection("DataSource=:memory:");
        await sql.OpenAsync();
        await using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(sql).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User { Id = 42, Username = "u42", Email = "u42@example.invalid", FullName = "Student" });
        await db.SaveChangesAsync();
        db.OutboxMessages.RemoveRange(db.OutboxMessages);
        await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.UserProfiles.Add(new CanonicalUserProfile { UserId = 42, DeclaredMajor = "Software engineering" });
            await db.SaveChangesAsync();
            var message = Assert.Single(await db.OutboxMessages.Where(x => x.EventType == RecommendationChangeCapture.Route).ToArrayAsync());
            Assert.Equal(42, JsonDocument.Parse(message.Payload).RootElement.GetProperty("userIds")[0].GetInt32());
            Assert.DoesNotContain("Software", message.Payload);
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Empty(await db.UserProfiles.ToArrayAsync());
        Assert.Empty(await db.OutboxMessages.ToArrayAsync());
    }
}
