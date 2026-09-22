using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using ActivityService.Services;
using ClubReportHub.Shared.Auth;
using ClubService.Contracts;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Infrastructure;
using ClubService.Models;
using FinanceService.Models;
using FinanceService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportService.Services;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class ArchitectureResilienceTests
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

    private sealed class FailingMockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("ActivityService connection refused (simulated outage)");
        }
    }

    [Fact]
    public async Task ClubService_ListMembers_DegradesGracefully_WhenActivityStatisticsFails()
    {
        // Arrange
        var dbName = $"ClubResilienceTest_{Guid.NewGuid():N}";
        var dbOptions = new DbContextOptionsBuilder<ClubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using var db = new ClubDbContext(dbOptions);

        var club = new Club
        {
            Id = 101,
            Name = "Resilience Club",
            Code = "RC101",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Clubs.Add(club);

        var managerMembership = new ClubMembership
        {
            Id = 1,
            ClubId = 101,
            UserId = 10,
            FullName = "Club Leader",
            Email = "leader@fpt.edu.vn",
            PhoneNumber = "0912345678",
            Role = "MANAGER",
            Status = ClubMembershipStatuses.Approved,
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMonths(-2),
            ReviewedAtUtc = DateTimeOffset.UtcNow.AddMonths(-2)
        };
        db.ClubMemberships.Add(managerMembership);

        var member2 = new ClubMembership
        {
            Id = 2,
            ClubId = 101,
            UserId = 20,
            FullName = "Club Member",
            Email = "member@fpt.edu.vn",
            PhoneNumber = "0987654321",
            Role = "MEMBER",
            Status = ClubMembershipStatuses.Approved,
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMonths(-1),
            ReviewedAtUtc = DateTimeOffset.UtcNow.AddMonths(-1)
        };
        db.ClubMemberships.Add(member2);

        db.ClubManagerAssignments.Add(new ClubManagerAssignment
        {
            Id = 1,
            ClubId = 101,
            ManagerUserId = 10,
            ManagerName = "Club Leader",
            AssignedAtUtc = DateTimeOffset.UtcNow.AddMonths(-2),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(db);

        builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", options =>
        {
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "ClubReportHub",
                ValidateAudience = true,
                ValidAudience = "ClubReportHub.Client",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = false
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.BusinessAccess, policy => policy.RequireAuthenticatedUser());
        });

        var failingHttpClient = new HttpClient(new FailingMockHandler())
        {
            BaseAddress = new Uri("http://activityservice.internal")
        };
        builder.Services.AddSingleton(new ActivityStatisticsClient(failingHttpClient));

        builder.WebHost.UseTestServer();
        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberManagementEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(10, AuthRoles.ClubManager));

        // Act - Call list members endpoint while ActivityService is DOWN
        var response = await client.GetAsync("/api/clubs/101/members");

        // Assert - Graceful degradation: Expect 200 OK with members and zeroed statistics, NOT 503
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pagedResult = await response.Content.ReadFromJsonAsync<PagedClubMembersResponse>();
        Assert.NotNull(pagedResult);
        Assert.Equal(2, pagedResult.TotalItems);
        Assert.Equal(2, pagedResult.Items.Count);

        // Participation stats degrade to 0 without breaking the member management page
        foreach (var item in pagedResult.Items)
        {
            Assert.NotNull(item.Participation);
            Assert.Equal(0, item.Participation.EligibleActivities);
            Assert.Equal(0, item.Participation.AttendedActivities);
            Assert.Equal(0m, item.Participation.ParticipationRate);
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task ClubService_GetMemberDetails_DegradesGracefully_WhenActivityStatisticsFails()
    {
        // Arrange
        var dbName = $"ClubResilienceMemberDetailTest_{Guid.NewGuid():N}";
        var dbOptions = new DbContextOptionsBuilder<ClubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using var db = new ClubDbContext(dbOptions);

        var club = new Club
        {
            Id = 102,
            Name = "Resilience Club 2",
            Code = "RC102",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Clubs.Add(club);

        var member = new ClubMembership
        {
            Id = 5,
            ClubId = 102,
            UserId = 50,
            FullName = "Target Member",
            Email = "target@fpt.edu.vn",
            PhoneNumber = "0911223344",
            Role = "MEMBER",
            Status = ClubMembershipStatuses.Approved,
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMonths(-1),
            ReviewedAtUtc = DateTimeOffset.UtcNow.AddMonths(-1)
        };
        db.ClubMemberships.Add(member);

        db.ClubManagerAssignments.Add(new ClubManagerAssignment
        {
            Id = 2,
            ClubId = 102,
            ManagerUserId = 10,
            ManagerName = "Club Leader",
            AssignedAtUtc = DateTimeOffset.UtcNow.AddMonths(-2),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(db);

        builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", options =>
        {
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "ClubReportHub",
                ValidateAudience = true,
                ValidAudience = "ClubReportHub.Client",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = false
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.BusinessAccess, policy => policy.RequireAuthenticatedUser());
        });

        var failingHttpClient = new HttpClient(new FailingMockHandler())
        {
            BaseAddress = new Uri("http://activityservice.internal")
        };
        builder.Services.AddSingleton(new ActivityStatisticsClient(failingHttpClient));

        builder.WebHost.UseTestServer();
        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMemberManagementEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(10, AuthRoles.ClubManager));

        // Act - Call member details endpoint while ActivityService is DOWN
        var response = await client.GetAsync("/api/clubs/102/members/5");

        // Assert - Graceful degradation: Expect 200 OK with member info and empty history
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<ClubMemberDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(50, detail.Member.UserId);
        Assert.Equal(0, detail.Participation.EligibleActivities);
        Assert.Equal(0, detail.Participation.AttendedActivities);
        Assert.Empty(detail.ActivityHistory);

        await app.StopAsync();
    }

    [Fact]
    public void AttendanceManagementRules_ValidateEligibility_EnforcesClubIsolation()
    {
        var joinedAt = DateTimeOffset.UtcNow.AddMonths(-1);
        var activityDate = DateTimeOffset.UtcNow;

        // Cross-club attempt: user belongs to club 1, activity belongs to club 2
        var crossClubResult = AttendanceManagementRules.ValidateEligibility(1, 2, joinedAt, activityDate);
        Assert.Equal("The activity belongs to another club.", crossClubResult);

        // Valid same-club attempt: user belongs to club 1, activity belongs to club 1
        var validResult = AttendanceManagementRules.ValidateEligibility(1, 1, joinedAt, activityDate);
        Assert.Null(validResult);
    }

    [Fact]
    public void FutureEventReportRules_Validate_EnforcesDateAndStructureInvariants()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Past date must be rejected
        var pastDetails = new[]
        {
            new FutureEventDetailInput(today.AddDays(-1), "Past Workshop", "Description", "Hall A")
        };
        var pastResult = FutureEventReportRules.Validate(FutureEventReportRules.ReportType, pastDetails, today);
        Assert.Equal("The planned event date must be in the future (Vietnam time).", pastResult);

        // Multiple details must be rejected (must be exactly 1)
        var multiDetails = new[]
        {
            new FutureEventDetailInput(today.AddDays(5), "Event 1", "Desc", "Loc"),
            new FutureEventDetailInput(today.AddDays(10), "Event 2", "Desc", "Loc")
        };
        var multiResult = FutureEventReportRules.Validate(FutureEventReportRules.ReportType, multiDetails, today);
        Assert.Equal("A future event proposal must contain exactly one planned event.", multiResult);

        // Valid future event
        var validDetails = new[]
        {
            new FutureEventDetailInput(today.AddDays(7), "Tech Talk", "AI in Healthcare", "Building Alpha")
        };
        var validResult = FutureEventReportRules.Validate(FutureEventReportRules.ReportType, validDetails, today);
        Assert.Null(validResult);
    }

    [Fact]
    public void BudgetProposalReviewRules_EnforcesWorkflowStateProgression()
    {
        // Manager can only review Submitted status
        Assert.True(BudgetProposalReviewRules.CanManagerReview(FinanceStatuses.Submitted));
        Assert.False(BudgetProposalReviewRules.CanManagerReview(FinanceStatuses.ManagerApproved));
        Assert.False(BudgetProposalReviewRules.CanManagerReview(FinanceStatuses.Approved));
        Assert.False(BudgetProposalReviewRules.CanManagerReview(FinanceStatuses.Rejected));

        // Final reviewer can only review ManagerApproved status
        Assert.True(BudgetProposalReviewRules.CanFinalReview(FinanceStatuses.ManagerApproved));
        Assert.False(BudgetProposalReviewRules.CanFinalReview(FinanceStatuses.Submitted));
        Assert.False(BudgetProposalReviewRules.CanFinalReview(FinanceStatuses.Approved));
        Assert.False(BudgetProposalReviewRules.CanFinalReview(FinanceStatuses.Rejected));
    }
}
