using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AuthService.Data;
using AuthService.Endpoints;
using AuthService.Models;
using AuthService.Services;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using SkiaSharp;

namespace Backend.StabilizationTests;

public sealed class SelfProfileMediaContractTests
{
    private const string Key = "SelfProfileMedia-Only-Long-Test-Signing-Key-2026";
    private const string Secret = "self-media-internal-test-only";
    private const string Profile = "/api/users/me/profile";

    [Theory]
    [InlineData("student-avatar", "avatarUrl")]
    [InlineData("student-cover", "coverImageUrl")]
    public async Task SelfImage_OwnedVerifiedRoundTripImmutableBytesAndNullClear(string kind, string field)
    {
        await using var f = await Fixture.Create(); using var actor = f.Client();
        var intent = await f.Intent(actor, kind); f.Storage.Upload(intent);
        var saved = await Send(actor, HttpMethod.Patch, Profile, Patch(Guid.Empty, field, intent.GetProperty("publicUrl").GetString()), "save");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode); var response = await Json(saved); var url = response.GetProperty("profile").GetProperty(field).GetString()!;
        Assert.Contains("/verified/", url); Assert.NotEqual(intent.GetProperty("publicUrl").GetString(), url);
        f.Storage.Staging[intent.GetProperty("key").GetString()!] = [0, 1, 2];
        using var reloaded = f.Client(); var profile = await Json(await reloaded.GetAsync(Profile)); Assert.Equal(url, profile.GetProperty("profile").GetProperty(field).GetString());
        Assert.Equal(f.Storage.Png, f.Storage.Verified[url]); Assert.Equal(1, f.Storage.VerificationCount);
        var cleared = await Send(actor, HttpMethod.Patch, Profile, Patch(profile.GetProperty("version").GetGuid(), field, null), "clear");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode); Assert.Equal(JsonValueKind.Null, (await Json(cleared)).GetProperty("profile").GetProperty(field).ValueKind);
    }

    [Fact]
    public async Task SelfImage_CrossAccountKindAndArbitraryUrlCannotAttachAndClubImagesStayProtected()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client(); using var other = f.Client(101);
        var intent = await f.Intent(owner, "student-avatar"); f.Storage.Upload(intent); var url = intent.GetProperty("publicUrl").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(other, HttpMethod.Patch, Profile, Patch(Guid.Empty, "avatarUrl", url), "other")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Patch, Profile, Patch(Guid.Empty, "coverImageUrl", url), "kind")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Patch, Profile, Patch(Guid.Empty, "avatarUrl", "https://unissued.example/photo.png"), "url")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, HttpMethod.Post, "/api/media/upload-intents", new { kind = "student-avatar", ownerId = "101", contentType = "image/png", sizeBytes = f.Storage.Png.Length }, "foreign-intent")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(owner, HttpMethod.Post, "/api/media/upload-intents", new { kind = "club-logo", ownerId = "1", contentType = "image/png", sizeBytes = f.Storage.Png.Length }, "club-intent")).StatusCode);
        await f.Db(async db => Assert.Equal(0, await db.UserProfiles.CountAsync()));
    }

    [Fact]
    public async Task SelfImage_ExpiredInvalidAndUnavailableObjectLeaveNoProfileAndCanRetryAfterFailure()
    {
        await using var f = await Fixture.Create(); using var actor = f.Client(); var expired = await f.Intent(actor, "student-avatar");
        await f.Db(async db => { (await db.PublicMediaIntents.SingleAsync()).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Conflict, (await Send(actor, HttpMethod.Patch, Profile, Patch(Guid.Empty, "avatarUrl", expired.GetProperty("publicUrl").GetString()), "expired")).StatusCode);
        var intent = await f.Intent(actor, "student-avatar"); var payload = Patch(Guid.Empty, "avatarUrl", intent.GetProperty("publicUrl").GetString());
        f.Storage.Staging[intent.GetProperty("key").GetString()!] = [1, 2, 3];
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(actor, HttpMethod.Patch, Profile, payload, "retry")).StatusCode);
        f.Storage.Upload(intent); f.Storage.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Send(actor, HttpMethod.Patch, Profile, payload, "retry")).StatusCode);
        await f.Db(async db => Assert.Equal(0, await db.UserProfiles.CountAsync()));
        f.Storage.Unavailable = false; f.Storage.Timeout = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Send(actor, HttpMethod.Patch, Profile, payload, "retry")).StatusCode);
        f.Storage.Timeout = false; Assert.Equal(HttpStatusCode.OK, (await Send(actor, HttpMethod.Patch, Profile, payload, "retry")).StatusCode);
    }

    [Fact]
    public async Task SelfImage_ReplayDoesNotReverifyAndLockedAccountCannotReuseReceiptOrCreateIntent()
    {
        await using var f = await Fixture.Create(); using var actor = f.Client(); var intent = await f.Intent(actor, "student-avatar"); f.Storage.Upload(intent);
        var payload = Patch(Guid.Empty, "avatarUrl", intent.GetProperty("publicUrl").GetString());
        var saved = await Send(actor, HttpMethod.Patch, Profile, payload, "save"); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); var receipt = await saved.Content.ReadAsStringAsync();
        Assert.Equal(receipt, await (await Send(actor, HttpMethod.Patch, Profile, payload, "save")).Content.ReadAsStringAsync()); Assert.Equal(1, f.Storage.VerificationCount);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(actor, HttpMethod.Patch, Profile, payload, "stale")).StatusCode);
        await f.Db(async db => { (await db.Users.SingleAsync(x => x.Id == 100)).IsLocked = true; await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(actor, HttpMethod.Patch, Profile, payload, "save")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(actor, HttpMethod.Post, "/api/media/upload-intents", new { kind = "student-avatar", ownerId = "100", contentType = "image/png", sizeBytes = f.Storage.Png.Length }, "locked")).StatusCode);
    }

    [Fact]
    public async Task InternalVerification_RequiresTokenPrincipalAndSelfOwnerAndKeepsPrivateProjectionMinimal()
    {
        await using var f = await Fixture.Create(); using var owner = f.Client(); using var other = f.Client(101);
        var intent = await f.Intent(owner, "student-cover"); f.Storage.Upload(intent); var input = new { kind = "student-cover", ownerId = 100, publicUrl = intent.GetProperty("publicUrl").GetString() };
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/internal/media/verify", input)).StatusCode);
        other.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", Secret); Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync("/api/internal/media/verify", input)).StatusCode);
        owner.DefaultRequestHeaders.Add("X-Internal-Workflow-Token", Secret); var verified = await owner.PostAsJsonAsync("/api/internal/media/verify", input); Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var url = (await Json(verified)).GetProperty("publicUrl").GetString();
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Patch, Profile, Patch(Guid.Empty, "coverImageUrl", url), "attach")).StatusCode);
        var privateRead = await Json(await owner.PostAsJsonAsync("/api/internal/experience/user-profiles", new[] { 100 }));
        Assert.False(privateRead[0].GetProperty("profile").GetProperty("profile").TryGetProperty("coverImageUrl", out _));
    }

    [Theory]
    [InlineData("club-logo")]
    [InlineData("club-cover")]
    [InlineData("activity-cover")]
    public async Task ManagedImage_IntentAndInternalAttachmentPreserveOwnerKindAndVerifiedBytes(string kind)
    {
        await using var f = await Fixture.Create(manage: true);
        using var actor = f.Client();
        var intent = await f.Intent(actor, kind);
        f.Storage.Upload(intent);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers.Authorization = actor.DefaultRequestHeaders.Authorization!.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Security:InternalWorkflowToken"] = Secret }).Build();
        using var internalHttp = f.Client();
        var attachments = new ClubReportHub.Shared.Experience.MediaAttachmentClient(internalHttp, configuration);
        var staging = intent.GetProperty("publicUrl").GetString()!;
        var saved = await attachments.VerifyAsync(staging, null, kind, 100, context, default);
        Assert.Equal(200, saved.StatusCode);
        Assert.Contains("/verified/", saved.PublicUrl);
        Assert.NotEqual(staging, saved.PublicUrl);
        Assert.Equal(f.Storage.Png, f.Storage.Verified[saved.PublicUrl!]);
        var replay = await attachments.VerifyAsync(saved.PublicUrl, null, kind, 100, context, default);
        Assert.Equal(saved.PublicUrl, replay.PublicUrl);
        Assert.Equal(1, f.Storage.VerificationCount);
        Assert.Equal(409, (await attachments.VerifyAsync(staging, null, kind, 101, context, default)).StatusCode);
        Assert.Equal(409, (await attachments.VerifyAsync(staging, null, "student-cover", 100, context, default)).StatusCode);
        using var other = f.Client(101);
        context.Request.Headers.Authorization = other.DefaultRequestHeaders.Authorization!.ToString();
        Assert.Equal(409, (await attachments.VerifyAsync(staging, null, kind, 100, context, default)).StatusCode);
    }

    [Fact]
    public async Task SqlServer_ImageMigrationPreservesExistingPrivateAndTextProfile()
    {
        await using var f = await Fixture.Create(legacy: true); using var actor = f.Client(); var read = await Json(await actor.GetAsync(Profile));
        Assert.Equal("Preserved", read.GetProperty("profile").GetProperty("headline").GetString());
        Assert.Equal("2004-02-29", read.GetProperty("personal").GetProperty("dateOfBirth").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("profile").GetProperty("avatarUrl").ValueKind);
        await f.Db(async db => Assert.False(db.Database.HasPendingModelChanges()));
    }

    private static object Patch(Guid version, string field, string? value) => new { expectedVersion = version, profile = new Dictionary<string, object?> { [field] = value } };
    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string key)
    { var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) }; request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }
    private sealed class DeniedClubAccess(bool manage = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(manage
                    ? new[] { new ClubAccessSnapshot(100, "Managed club", true, false, true, new[] { 100 }) }
                    : Array.Empty<ClubAccessSnapshot>())
            });
    }
    private sealed class ActivityAuthority : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { clubId = 100 }) });
    }
    private sealed class Storage : IPublicMediaStorage
    {
        internal readonly Dictionary<string, byte[]> Staging = new(); internal readonly Dictionary<string, byte[]> Verified = new();
        internal byte[] Png { get; }
        internal int VerificationCount { get; private set; }
        internal bool Unavailable { get; set; }
        internal bool Timeout { get; set; }
        internal Storage() { using var bitmap = new SKBitmap(4, 4); bitmap.Erase(SKColors.CornflowerBlue); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); Png = data.ToArray(); }
        internal void Upload(JsonElement intent) => Staging[intent.GetProperty("key").GetString()!] = Png.ToArray();
        public string PublicUrl(string key) => "https://media.example.test/" + key;
        public Task<string> SignAsync(PublicMediaIntent intent, CancellationToken ct) => Task.FromResult("https://test.r2.cloudflarestorage.com/bucket/" + intent.Key + "?signature=test");
        public Task<string> VerifyAsync(PublicMediaIntent intent, CancellationToken ct)
        {
            if (Unavailable) throw new HttpRequestException("Test storage unavailable.");
            if (Timeout) throw new TaskCanceledException("Test storage timeout.");
            if (!Staging.TryGetValue(intent.Key, out var bytes) || bytes.Length != intent.SizeBytes) throw new InvalidDataException();
            PublicMediaStorage.Validate(bytes, intent.ContentType); VerificationCount++;
            var url = PublicUrl("verified/" + Guid.NewGuid().ToString("N") + ".png"); Verified[url] = bytes.ToArray(); return Task.FromResult(url);
        }
    }
    private sealed class Fixture(WebApplication app, string connection, Storage storage) : IAsyncDisposable
    {
        internal Storage Storage => storage;
        internal static async Task<Fixture> Create(bool legacy = false, bool manage = false)
        {
            var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ADMIN_SERVICE_TEST_CONNECTION_STRING") ?? "Server=(localdb)\\ClubManagerApiTests;Integrated Security=True;TrustServerCertificate=True") { InitialCatalog = "ClubHub_SelfMediaTests_" + Guid.NewGuid().ToString("N") }.ConnectionString;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "ClubReportHub", ["Jwt:Audience"] = "ClubReportHub.Client", ["Jwt:SigningKey"] = Key, ["Security:InternalWorkflowToken"] = Secret });
            builder.Services.AddDbContext<AuthDbContext>(x => x.UseSqlServer(connection)); builder.Services.AddClubReportJwtValidation(builder.Configuration, builder.Environment);
            var storage = new Storage(); builder.Services.AddSingleton<IPublicMediaStorage>(storage); builder.Services.AddHttpClient();
            builder.Services.AddSingleton(new ClubAccessClient(new HttpClient(new DeniedClubAccess(manage)) { BaseAddress = new Uri("http://club/") }, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<ClubAccessClient>.Instance));
            builder.Services.AddHttpClient("MediaActivity", client => client.BaseAddress = new Uri("http://activity/"))
                .ConfigurePrimaryHttpMessageHandler(() => new ActivityAuthority());
            var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapCanonicalProfileEndpoints(); app.MapPublicMediaEndpoints(); var f = new Fixture(app, connection, storage);
            try
            {
                await f.Db(async db =>
                {
                    await db.GetService<IMigrator>().MigrateAsync("20261022152518_SelfProfilePrivateExtensions");
                    await db.Database.OpenConnectionAsync(); await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Users] ON");
                    foreach (var id in new[] { 100, 101 }) db.Add(new User { Id = id, Username = "image" + id, Email = id + "@fpt.edu.vn", FullName = "Account " + id, CampusCode = "HAN", IsActive = true });
                    await HistoricalAuthFixture.SeedTrackedUsersAsync(db); await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT [Users] OFF");
                    if (legacy) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [UserProfiles] ([UserId],[Version],[DisplayName],[About],[SkillsJson],[InterestsJson],[PhoneNumber],[UpdatedAtUtc],[Headline],[DateOfBirth],[Address]) VALUES ({100},{Guid.NewGuid()},{"Legacy"},{"About"},{"[]"},{"[]"},{"0901234567"},{DateTimeOffset.UtcNow},{"Preserved"},{"2004-02-29"},{"Address"})");
                    await db.Database.CloseConnectionAsync(); await db.Database.MigrateAsync();
                }); await app.StartAsync(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        internal HttpClient Client(int id = 100)
        {
            var token = new JwtSecurityToken("ClubReportHub", "ClubReportHub.Client", [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.Role, AuthRoles.ClubMember)], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
            var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token)); return client;
        }
        internal async Task<JsonElement> Intent(HttpClient client, string kind)
        {
            var response = await Send(client, HttpMethod.Post, "/api/media/upload-intents", new { kind, ownerId = "100", contentType = "image/png", sizeBytes = storage.Png.Length }, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await Json(response);
        }
        internal async Task Db(Func<AuthDbContext, Task> action) { await using var scope = app.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AuthDbContext>()); }
        public async ValueTask DisposeAsync()
        {
            if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("ClubHub_SelfMediaTests_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test database.");
            await Db(async db => await db.Database.EnsureDeletedAsync()); await app.DisposeAsync();
        }
    }
}
