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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Backend.StabilizationTests;

public sealed class StudentInspectionTests
{
    [Fact]
    public async Task ProfileUsesSelectedRosterAndApprovedMembershipSourceWithoutInventingAcademicFields()
    {
        await using var f = await Fixture.Create(); using var client = f.Client(900, AuthRoles.StudentAffairsAdmin);
        var response = await client.GetAsync("/api/v1/student-affairs/students/100/profile?semester=FALL2026");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SE100", body.GetProperty("studentCode").GetString()); Assert.Equal("AVAILABLE", body.GetProperty("academicAvailability").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("major").ValueKind); Assert.Equal(JsonValueKind.Null, body.GetProperty("cohort").ValueKind);
        Assert.Equal("NO_AUTHORITATIVE_SOURCE", body.GetProperty("majorAvailability").GetString());
        var memberships = body.GetProperty("membership"); Assert.Equal("AVAILABLE", memberships.GetProperty("availability").GetString());
        Assert.Single(memberships.GetProperty("items").EnumerateArray()); Assert.Equal(7, memberships.GetProperty("items")[0].GetProperty("clubId").GetInt32());
        Assert.DoesNotContain("phone", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProfileRejectsOtherCampusNonstudentsAndUnprivilegedActorsBeforeMembershipLookup()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(900, AuthRoles.StudentAffairsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/student-affairs/students/105/profile?semester=FALL2026")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/v1/student-affairs/students/999/profile")).StatusCode);
        using var student = f.Client(100, AuthRoles.ClubMember);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/v1/student-affairs/students/100/profile")).StatusCode);
        using var locked = f.Client(902, AuthRoles.StudentAffairsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await locked.GetAsync("/api/v1/student-affairs/students/100/profile")).StatusCode);
        Assert.Equal(0, f.Authority.MembershipCalls);
        using var admin = f.Client(901, AuthRoles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/student-affairs/students/105/profile?semester=FALL2026")).StatusCode);
    }

    [Fact]
    public async Task MissingSemesterChangedIdentityAndUnavailableMembershipAreNotKnownEmptyData()
    {
        await using var f = await Fixture.Create(); using var client = f.Client(900, AuthRoles.StudentAffairsAdmin);
        var noPeriod = await client.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/students/100/profile");
        Assert.Equal("SEMESTER_REQUIRED", noPeriod.GetProperty("academicAvailability").GetString());
        Assert.Equal(JsonValueKind.Null, noPeriod.GetProperty("studentCode").ValueKind);
        f.Authority.StudentEmail = "changed@fpt.edu.vn";
        f.Authority.MembershipUnavailable = true;
        var changed = await client.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/students/100/profile?semester=FALL2026");
        Assert.Equal("IDENTITY_CHANGED", changed.GetProperty("academicAvailability").GetString());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("studentCode").ValueKind);
        Assert.Equal("AUTHORITY_UNAVAILABLE", changed.GetProperty("membership").GetProperty("availability").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/student-affairs/students/100/profile?semester=UNKNOWN")).StatusCode);
    }

    [Fact]
    public async Task CampusScopeUsesCurrentAccountAndOnlyAdminCanSelectAllFiveCampuses()
    {
        await using var f = await Fixture.Create(); using var staff = f.Client(900, AuthRoles.StudentAffairsAdmin);
        var scope = await staff.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/campuses");
        Assert.False(scope.GetProperty("canSelectCampus").GetBoolean()); Assert.Single(scope.GetProperty("campuses").EnumerateArray());
        Assert.Equal("HAN", scope.GetProperty("campuses")[0].GetProperty("code").GetString());
        f.Authority.StaffCampus = "HCM";
        scope = await staff.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/campuses");
        Assert.Equal("HCM", scope.GetProperty("campuses")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/student-affairs/students/100/profile")).StatusCode);
        using var admin = f.Client(901, AuthRoles.Admin);
        scope = await admin.GetFromJsonAsync<JsonElement>("/api/v1/student-affairs/campuses");
        Assert.True(scope.GetProperty("canSelectCampus").GetBoolean()); Assert.Equal(5, scope.GetProperty("campuses").GetArrayLength());
    }

    private sealed class Authority : HttpMessageHandler
    {
        internal int MembershipCalls; internal bool MembershipUnavailable; internal string StaffCampus = "HAN";
        internal string StudentEmail = "student100@fpt.edu.vn";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.Contains("accounts/resolve"))
            {
                var query = (await request.Content!.ReadFromJsonAsync<SchoolAccountQuery>(ct))!;
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(query.UserIds!.Select(id => new SchoolAccount(id,
                    id == 100 ? StudentEmail : $"student{id}@fpt.edu.vn", "Student " + id, id == 901 ? "GLOBAL" : id == 105 ? "HCM" : id == 900 ? StaffCampus : "HAN",
                    true, id == 902, id is 100 or 105, true)))
                };
            }
            MembershipCalls++;
            if (MembershipUnavailable) return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new StudentClubScope([
                new(7, "DEV", "Development Club", "TECH", "HAN", true, true, DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid()),
                new(8, "ART", "Other Club", "ART", "HAN", false, false, null, Guid.NewGuid(), null)
            ], DateTimeOffset.UtcNow, "membership-revision"))
            };
        }
    }

    private sealed class Fixture(WebApplication app, SqliteConnection connection, Authority authority) : IAsyncDisposable
    {
        internal WebApplication App => app; internal Authority Authority => authority;
        private const string Key = "test-student-inspection-only-signing-key-at-least-32-characters";
        internal static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ClubReportHub",
                ["Jwt:Audience"] = "ClubReportHub.Client",
                ["Jwt:SigningKey"] = Key,
                ["Security:InternalWorkflowToken"] = "test-inspection-internal-token"
            });
            builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            builder.Services.AddAuthorization(options => options.AddPolicy(AdminPolicies.BackofficeUser,
                policy => policy.RequireRole(AuthRoles.Admin, AuthRoles.StudentAffairsAdmin)));
            builder.Services.AddDbContext<AdminDbContext>(options => options.UseSqlite(connection));
            var authority = new Authority(); var client = new HttpClient(authority) { BaseAddress = new Uri("http://authority/") };
            builder.Services.AddSingleton(new SchoolAccountClient(client, builder.Configuration));
            builder.Services.AddSingleton(new ClubExperienceAuthorityClient(client, builder.Configuration));
            var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapStudentInspectionEndpoints();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>(); await db.Database.EnsureCreatedAsync();
                var semester = new Semester { SemesterCode = "FALL2026", IsActive = true };
                var roster = new SemesterRoster { SemesterId = semester.Id, CampusCode = "HAN", Version = Guid.NewGuid(), RevisionId = Guid.NewGuid() };
                db.Add(semester); db.Add(roster); db.Add(new SemesterRosterRevision { Id = roster.RevisionId, RosterId = roster.Id });
                db.Add(new SemesterRosterRow { RevisionId = roster.RevisionId, UserId = 100, Email = "student100@fpt.edu.vn", StudentCode = "SE100", CampusCode = "HAN", Active = true });
                await db.SaveChangesAsync();
            }
            await app.StartAsync(); return new(app, connection, authority);
        }
        internal HttpClient Client(int id, string role)
        {
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client",
                [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.Role, role), new Claim("campus", "HAN")],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        public async ValueTask DisposeAsync() { await app.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
