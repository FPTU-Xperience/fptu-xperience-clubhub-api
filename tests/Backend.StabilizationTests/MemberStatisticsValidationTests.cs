using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ActivityService.Contracts;
using ActivityService.Data;
using ActivityService.Endpoints;
using ActivityService.Infrastructure;
using ActivityService.Models;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Messaging;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Backend.StabilizationTests;

public sealed class MemberStatisticsValidationTests
{
    private const string SigningKey = "test-only-signing-key-at-least-32-characters";

    private static string CreateToken(int userId, string role = AuthRoles.ClubManager)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "ClubReportHub",
            Audience = "ClubReportHub.Client",
            SigningKey = SigningKey,
            ExpirationMinutes = 60
        });
        return new JwtTokenFactory(options).CreateToken(userId, $"user{userId}", $"User {userId}", [role]).AccessToken;
    }

    private sealed class DelegatingMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handlerFunc(request));
        }
    }

    private static ClubAccessClient CreateClubAccessClient(int clubId, int managerUserId)
    {
        var handler = new DelegatingMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<ClubAccessSnapshot>
            {
                new(clubId, "Test Club", true, false, true, [managerUserId])
            })
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5102/") };
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ClubAccessClient(httpClient, cache, NullLogger<ClubAccessClient>.Instance);
    }

    [Fact]
    public async Task ClubService_ResolveRosterMembers_ByUserIds_ReturnsOnlyApprovedClubMembers()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"club-resolve-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ClubDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberManagementEndpoints();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            db.Clubs.Add(new Club
            {
                Id = 1,
                Code = "CLUB1",
                Name = "Club 1",
                Category = "Academic",
                IsActive = true
            });
            // Manager for Club 1
            db.ClubManagerAssignments.Add(new ClubManagerAssignment
            {
                ClubId = 1,
                ManagerUserId = 100,
                ManagerName = "Manager 100",
                IsActive = true
            });
            // Member 10 in Club 1 (Approved)
            db.ClubMemberships.Add(new ClubMembership
            {
                Id = 1,
                ClubId = 1,
                UserId = 10,
                FullName = "Member 10",
                Role = ClubMemberRoles.Member,
                Status = ClubMembershipStatuses.Approved,
                ReviewedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
            });
            // Member 99 in Club 2 (Belongs to another club!)
            db.ClubMemberships.Add(new ClubMembership
            {
                Id = 2,
                ClubId = 2,
                UserId = 99,
                FullName = "Foreign User 99",
                Role = ClubMemberRoles.Member,
                Status = ClubMembershipStatuses.Approved,
                ReviewedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100));

        // Query with both Member 10 (valid) and User 99 (foreign club)
        var response = await client.PostAsJsonAsync("/api/clubs/1/member-roster/resolve", new
        {
            userIds = new[] { 10, 99 }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<ClubMemberRosterItemResponse>>();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(10, items[0].UserId);
        Assert.Equal("Member 10", items[0].FullName);
    }

    [Fact]
    public async Task ActivityService_BatchMemberStatistics_RejectsForeignUserAndUsesAuthoritativeJoinDate()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-stats-batch-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<MemberActivityStatisticsService>();
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(1, 100));

        // Mock ClubMemberRosterClient response from ClubService:
        // Only User 10 is an approved member of Club 1 with server join date 2026-06-01. User 99 is NOT in the club.
        var rosterHandler = new DelegatingMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<ClubMemberRosterItem>
            {
                new(1, 10, "Member 10", "m10@fpt.edu.vn", "0123456789", "Member", "Approved", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero))
            })
        });
        builder.Services.AddSingleton(new ClubMemberRosterClient(new HttpClient(rosterHandler) { BaseAddress = new Uri("http://localhost:5102/") }));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberStatisticsEndpoints();

        // Seed activities in ActivityDbContext:
        // Activity A: 2026-05-01 (BEFORE User 10 joined Club 1 on 2026-06-01)
        // Activity B: 2026-07-01 (AFTER User 10 joined Club 1)
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            db.Activities.AddRange(
                new ClubActivity
                {
                    Id = 1,
                    ClubId = 1,
                    ClubName = "Club 1",
                    Title = "Pre-Join Activity",
                    StartTimeUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                    Status = ActivityStatuses.Completed,
                    Location = "Hall A"
                },
                new ClubActivity
                {
                    Id = 2,
                    ClubId = 1,
                    ClubName = "Club 1",
                    Title = "Post-Join Activity",
                    StartTimeUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                    Status = ActivityStatuses.Completed,
                    Location = "Hall B"
                }
            );
            // User 10 attended Activity 2
            db.ActivityAttendances.Add(new ActivityAttendance
            {
                ActivityId = 2,
                UserId = 10,
                FullName = "Member 10",
                AttendanceDate = new DateOnly(2026, 7, 1),
                Status = AttendanceStatuses.Present
            });
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100));

        // Malicious or caller-supplied query:
        // 1. Attempts to probe statistics for User 99 (who is NOT in Club 1)
        // 2. Fabricates JoinedAtUtc: 2020-01-01 for User 10 to include historical activities before they actually joined
        var response = await client.PostAsJsonAsync("/api/activities/clubs/1/member-statistics", new MemberStatisticsQuery(
        [
            new MemberStatisticsInput(10, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new MemberStatisticsInput(99, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))
        ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<MemberStatisticsResponse>>();
        Assert.NotNull(results);

        // SEC-F11 Assertion 1: User 99 is completely omitted because they are not a member of Club 1!
        Assert.Single(results);
        Assert.Equal(10, results[0].UserId);

        // SEC-F11 Assertion 2: The client-supplied 2020-01-01 join date was IGNORED.
        // The server used the authoritative 2026-06-01 join date.
        // Therefore, Activity 1 (from 2026-05-01) is NOT eligible. Only Activity 2 (2026-07-01) is eligible!
        Assert.Equal(1, results[0].EligibleActivities);
        Assert.Equal(1, results[0].AttendedActivities);
        Assert.Equal(100m, results[0].ParticipationRate);
    }

    [Fact]
    public async Task ActivityService_DetailStatistics_Returns404ForUserOutsideClub()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-stats-detail-404-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<MemberActivityStatisticsService>();
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(1, 100));

        // Mock ClubMemberRosterClient returns empty (user 99 is NOT an approved member of club 1)
        var rosterHandler = new DelegatingMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<ClubMemberRosterItem>())
        });
        builder.Services.AddSingleton(new ClubMemberRosterClient(new HttpClient(rosterHandler) { BaseAddress = new Uri("http://localhost:5102/") }));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberStatisticsEndpoints();

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100));

        // Query details for User 99 in Club 1
        var response = await client.PostAsJsonAsync("/api/activities/clubs/1/member-statistics/detail", new MemberStatisticsDetailQuery(
            99,
            DateTimeOffset.UtcNow,
            1,
            20));

        // SEC-F11 Assertion: Must return 404 Not Found because user 99 does not belong to Club 1
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivityService_DetailStatistics_UsesAuthoritativeJoinDateOverridingClientInput()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ClubReportHub",
            ["Jwt:Audience"] = "ClubReportHub.Client",
            ["Jwt:SigningKey"] = SigningKey
        });

        var dbName = $"activity-stats-detail-auth-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ActivityDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<MemberActivityStatisticsService>();
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(Substitute.For<IEventBus>());
        builder.Services.AddSingleton(CreateClubAccessClient(1, 100));

        // True join date in ClubService is 2026-06-01
        var rosterHandler = new DelegatingMockHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<ClubMemberRosterItem>
            {
                new(1, 10, "Member 10", "m10@fpt.edu.vn", "0123456789", "Member", "Approved", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero))
            })
        });
        builder.Services.AddSingleton(new ClubMemberRosterClient(new HttpClient(rosterHandler) { BaseAddress = new Uri("http://localhost:5102/") }));

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberStatisticsEndpoints();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
            db.Activities.AddRange(
                new ClubActivity
                {
                    Id = 1,
                    ClubId = 1,
                    ClubName = "Club 1",
                    Title = "Pre-Join Activity",
                    StartTimeUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                    Status = ActivityStatuses.Completed,
                    Location = "Hall A"
                },
                new ClubActivity
                {
                    Id = 2,
                    ClubId = 1,
                    ClubName = "Club 1",
                    Title = "Post-Join Activity",
                    StartTimeUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                    Status = ActivityStatuses.Completed,
                    Location = "Hall B"
                }
            );
            await db.SaveChangesAsync();
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(100));

        // Client supplies fake JoinedAtUtc: 2020-01-01
        var response = await client.PostAsJsonAsync("/api/activities/clubs/1/member-statistics/detail", new MemberStatisticsDetailQuery(
            10,
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1,
            20));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<MemberStatisticsDetailResponse>();
        Assert.NotNull(detail);

        // Pre-join activity (2026-05-01) must NOT be eligible because authoritative join date is 2026-06-01
        Assert.Equal(1, detail.Statistics.EligibleActivities);
        Assert.Single(detail.Items);
        Assert.Equal(2, detail.Items.First().ActivityId);
    }
}
