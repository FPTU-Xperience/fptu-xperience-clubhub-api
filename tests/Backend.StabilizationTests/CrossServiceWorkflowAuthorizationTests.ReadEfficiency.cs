using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubReportHub.Shared.Experience;
using ClubService.Data;
using ClubService.Endpoints;
using ClubService.Mappers;
using ClubService.Models;
using ExportService.Data;
using ExportService.Endpoints;
using ExportService.Models;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task ReadEfficiency_ClubDirectoryCountsInSqlWithoutMaterializingMemberProfiles()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var capture = new ReadEfficiencyCapture();
        await using var app = await CreateReadEfficiencyClubApp(database.ConnectionString, capture);
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);

        capture.Clear();
        var clubs = await student.GetFromJsonAsync<JsonElement[]>("/api/clubs");
        Assert.NotNull(clubs);
        Assert.Equal(new[] { 1, 4 }, clubs.Select(x => x.GetProperty("id").GetInt32()));
        var directory = clubs[0];
        Assert.Equal(1, directory.GetProperty("memberCount").GetInt32());
        Assert.Equal(new[] { 101, 201 }, directory.GetProperty("managers").EnumerateArray()
            .Select(x => x.GetProperty("managerUserId").GetInt32()));
        Assert.False(directory.TryGetProperty("members", out _));
        Assert.DoesNotContain("private-member@example.test", directory.GetRawText());
        Assert.Equal(0, capture.MaterializedMemberships);
        Assert.Contains(capture.Commands, sql => sql.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("ClubMemberships", StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains(".[FullName]", StringComparison.Ordinal)
            || sql.Contains(".[Email]", StringComparison.Ordinal) || sql.Contains(".[PhoneNumber]", StringComparison.Ordinal));

        var owned = await manager.GetFromJsonAsync<JsonElement[]>("/api/clubs");
        Assert.Equal(new[] { 1, 2, 4 }, owned!.Select(x => x.GetProperty("id").GetInt32()));
        var filtered = await student.GetFromJsonAsync<JsonElement[]>(
            "/api/clubs?search=Alpha&category=TECHNOLOGY&campus=HN&active=true&recruiting=true");
        Assert.Equal(1, Assert.Single(filtered!).GetProperty("id").GetInt32());

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var legacy = await db.Clubs.AsNoTracking().Include(x => x.ManagerAssignments).Include(x => x.Memberships)
            .AsSplitQuery().SingleAsync(x => x.Id == 1);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(ClubMappers.ToDirectoryResponse(legacy), options), directory.GetRawText());
        Assert.Equal(JsonSerializer.Serialize(ClubMappers.ToDirectoryResponse(legacy), options),
            JsonSerializer.Serialize(ClubMappers.ToDirectoryResponse(legacy, 1), options));
    }

    [Fact]
    public async Task ReadEfficiency_MemberPaginationClampsLargeOffsetsAndPreservesDefaultsAndScope()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var capture = new ReadEfficiencyCapture();
        await using var app = await CreateReadEfficiencyClubApp(database.ConnectionString, capture);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        // The owned inactive club uses the same membership policy; directory visibility is independent.
        foreach (var path in new[]
        {
            "/api/clubs/2/members",
            "/api/clubs/2/member-roster?joinedOnOrBefore=2030-01-01T00:00:00Z"
        })
        {
            var separator = path.Contains('?') ? '&' : '?';
            var huge = await manager.GetAsync($"{path}{separator}page={int.MaxValue}&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, huge.StatusCode);
            var body = await huge.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(int.MaxValue, body.GetProperty("page").GetInt32());
            Assert.Equal(100, body.GetProperty("pageSize").GetInt32());
            Assert.Empty(body.GetProperty("items").EnumerateArray());
            Assert.Equal(1, body.GetProperty("totalItems").GetInt32());
            var first = await manager.GetFromJsonAsync<JsonElement>($"{path}{separator}page=-3&pageSize=0");
            Assert.Equal(1, first.GetProperty("page").GetInt32());
            Assert.Equal(1, first.GetProperty("pageSize").GetInt32());
            Assert.Single(first.GetProperty("items").EnumerateArray());
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync("/api/clubs/1/members?page=2147483647&pageSize=100")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync("/api/clubs/1/member-roster?joinedOnOrBefore=2030-01-01T00:00:00Z&page=2147483647&pageSize=100")).StatusCode);
    }

    [Fact]
    public async Task ReadEfficiency_PdfStatusProjectsMetadataAndRechecksCurrentOwnership()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var capture = new ReadEfficiencyCapture();
        var ownership = new ReadEfficiencyOwnership();
        await using var app = await CreateReadEfficiencyExportApp(database.ConnectionString, capture, ownership);
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        using var outsider = CreateAdminContractClient(app, 101, AuthRoles.ClubManager);
        var snapshot = new ClubExperienceStatistics(1, 1, "Club one", "FALL2026", DateTimeOffset.UtcNow,
            "VERIFIED_NET_LEDGER", new(1, 1, 1, 1, 1, 1, 0), null, new Dictionary<string, TargetProgress>(),
            1, 1, new Dictionary<string, int>(), new Dictionary<string, decimal>(), [])
        { LedgerRevision = "TEST_LEDGER_REVISION" };
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var pdf = Encoding.ASCII.GetBytes("%PDF-test").Concat(new byte[1024 * 1024]).ToArray();
        var rows = new[] { "Pending", "Completed", "Failed" }.Select(status => new ClubExperienceReport
        {
            ClubId = 1,
            SemesterId = "FALL2026",
            CreatedBy = 100,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
            SnapshotJson = json,
            SnapshotHash = hash,
            PdfBytes = status == "Completed" ? pdf : null,
            FailureCode = status == "Failed" ? "TEST_FAILURE" : null
        }).ToArray();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            db.ClubExperienceReports.AddRange(rows);
            await db.SaveChangesAsync();
        }

        capture.Clear();
        foreach (var row in rows)
        {
            var response = await manager.GetAsync($"/api/club-experience-reports/{row.Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(row.Id, body.GetProperty("reportId").GetGuid());
            Assert.Equal(row.Status, body.GetProperty("status").GetString());
            Assert.Equal(hash, body.GetProperty("snapshotHash").GetString());
            Assert.Equal(snapshot.LedgerRevision, body.GetProperty("ledgerRevision").GetString());
            Assert.Equal(snapshot.GeneratedAt, body.GetProperty("asOf").GetDateTimeOffset());
            Assert.Equal(row.FailureCode, body.GetProperty("failureCode").GetString());
            Assert.Equal(row.Status == "Completed" ? $"/api/clubs/1/experience-reports/{row.Id}/download" : null,
                body.GetProperty("downloadUrl").GetString());
            Assert.False(body.TryGetProperty("pdfBytes", out _));
        }
        Assert.Equal(3, capture.Commands.Count);
        Assert.DoesNotContain(capture.Commands, sql => sql.Contains("PdfBytes", StringComparison.Ordinal));
        var completed = rows[1];
        var download = await manager.GetAsync($"/api/clubs/1/experience-reports/{completed.Id}/download");
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync($"/api/club-experience-reports/{completed.Id}")).StatusCode);
        ownership.Active = false;
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"/api/club-experience-reports/{completed.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.GetAsync($"/api/club-experience-reports/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ReadEfficiency_ExportPaginationClampsLargeOffsetsAndKeepsOwnJobs()
    {
        await using var database = await ManagerSqlDatabase.CreateAsync();
        var capture = new ReadEfficiencyCapture();
        await using var app = await CreateReadEfficiencyExportApp(database.ConnectionString, capture, new());
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
            foreach (var id in Enumerable.Range(1, 3))
            {
                db.ExportRequests.Add(new ExportRequest
                {
                    RequestedByUserId = id == 3 ? 101 : 100,
                    ExportType = "PDF",
                    Scope = "Report",
                    Status = "Pending",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-id)
                });
                await db.SaveChangesAsync();
            }
        }
        using var manager = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var hugeResponse = await manager.GetAsync($"/api/exports?page={int.MaxValue}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, hugeResponse.StatusCode);
        var huge = await hugeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, huge.GetProperty("total").GetInt32());
        Assert.Equal(int.MaxValue, huge.GetProperty("page").GetInt32());
        Assert.Empty(huge.GetProperty("items").EnumerateArray());
        foreach (var size in new[] { 0, -1, 101 })
        {
            var page = await manager.GetFromJsonAsync<JsonElement>($"/api/exports?page=-3&pageSize={size}");
            Assert.Equal(1, page.GetProperty("page").GetInt32());
            Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
            Assert.Equal(new[] { 1, 2 }, page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
        }
    }

    private static WebApplicationBuilder ReadEfficiencyBuilder()
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
        builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
        return builder;
    }

    private static async Task<WebApplication> CreateReadEfficiencyClubApp(string connection, ReadEfficiencyCapture capture)
    {
        var builder = ReadEfficiencyBuilder();
        builder.Services.AddDbContext<ClubDbContext>(options => options.UseSqlServer(connection).AddInterceptors(capture));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapClubEndpoints();
        app.MapMemberManagementEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            await db.Database.MigrateAsync();
            foreach (var club in new[]
            {
                new Club { Code = "ALPHA", Name = "Alpha", Category = "TECHNOLOGY", IsRecruiting = true },
                new Club { Code = "BETA", Name = "Beta", IsActive = false },
                new Club { Code = "HIDDEN", Name = "Hidden", IsActive = false },
                new Club { Code = "ZETA", Name = "Zeta" }
            })
            {
                db.Clubs.Add(club);
                await db.SaveChangesAsync();
            }
            db.ClubManagerAssignments.AddRange(
                new ClubManagerAssignment { ClubId = 1, ManagerUserId = 101, ManagerName = "Current", IsActive = true },
                new ClubManagerAssignment { ClubId = 1, ManagerUserId = 201, ManagerName = "Former", IsActive = false },
                new ClubManagerAssignment { ClubId = 2, ManagerUserId = 100, ManagerName = "Owner", IsActive = true });
            db.ClubMemberships.AddRange(
                new ClubMembership { ClubId = 1, UserId = 104, FullName = "Member", Email = "private-member@example.test", Status = ClubMembershipStatuses.Approved },
                new ClubMembership { ClubId = 1, UserId = 105, Status = ClubMembershipStatuses.Pending },
                new ClubMembership { ClubId = 1, UserId = 106, Status = ClubMembershipStatuses.Approved, IsDeleted = true },
                new ClubMembership { ClubId = 2, UserId = 107, FullName = "Eligible", Status = ClubMembershipStatuses.Approved, ReviewedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> CreateReadEfficiencyExportApp(string connection, ReadEfficiencyCapture capture,
        ReadEfficiencyOwnership ownership)
    {
        var builder = ReadEfficiencyBuilder();
        builder.Services.AddDbContext<ExportDbContext>(options => options.UseSqlServer(connection).AddInterceptors(capture));
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(Substitute.For<IBackgroundJobClient>());
        builder.Services.AddSingleton(new ClubAccessClient(CreateFakeHttpClient(request =>
        {
            var actor = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .ReadJwtToken(request.Headers.Authorization!.Parameter).Subject;
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new ClubAccessSnapshot(1, "Club one", ownership.Active && actor == "100", false, true, [100]) })
            };
        }), new MemoryCache(new MemoryCacheOptions()), NullLogger<ClubAccessClient>.Instance));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapClubExperienceReportEndpoints();
        app.MapExportEndpoints();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ExportDbContext>().Database.MigrateAsync();
        await app.StartAsync();
        return app;
    }

    private sealed class ReadEfficiencyOwnership
    {
        internal bool Active { get; set; } = true;
    }

    private sealed class ReadEfficiencyCapture : DbCommandInterceptor, IMaterializationInterceptor
    {
        internal ConcurrentQueue<string> Commands { get; } = new();
        internal int MaterializedMemberships { get; private set; }
        internal void Clear()
        {
            Commands.Clear();
            MaterializedMemberships = 0;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return new(result);
        }
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is ClubMembership) MaterializedMemberships++;
            return entity;
        }
    }
}
