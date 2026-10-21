using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClubReportHub.Shared.Auth;
using ClubService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.StabilizationTests;

public sealed partial class CrossServiceWorkflowAuthorizationTests
{
    [Fact]
    public async Task ClubContract_ManagerReadProvidesVersionImagesAndStructuredSchedule()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        var club = await client.GetFromJsonAsync<JsonElement>("/api/clubs/1");
        Assert.NotEqual(Guid.Empty, club.GetProperty("version").GetGuid());
        Assert.Equal("https://images.example/cover.png", club.GetProperty("coverImageUrl").GetString());
        Assert.Equal("FALL2026", club.GetProperty("schedule").GetProperty("semesterId").GetString());
        var managed = await client.GetFromJsonAsync<JsonElement>("/api/clubs/me/managed");
        Assert.Equal(club.GetProperty("version").GetGuid(), managed[0].GetProperty("version").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/clubs/1/public-profile",
            new { description = "From manager GET", expectedVersion = club.GetProperty("version").GetGuid() })).StatusCode);
        using var student = CreateAdminContractClient(app, 104, AuthRoles.ClubMember);
        var publicClub = await student.GetFromJsonAsync<JsonElement>("/api/clubs/1");
        Assert.False(publicClub.TryGetProperty("members", out _));
        Assert.False(publicClub.TryGetProperty("managers", out _));
    }

    [Theory]
    [InlineData("logoUrl", "coverImageUrl", "https://images.example/cover.png")]
    [InlineData("coverImageUrl", "logoUrl", "https://images.example/logo.png")]
    public async Task ClubContract_SingleImageChangePreservesTheOmittedImage(string changed, string preserved, string original)
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        await using var scope = app.Services.CreateAsyncScope();
        var version = (await scope.ServiceProvider.GetRequiredService<ClubDbContext>().Clubs.FindAsync(1))!.ConcurrencyToken;
        var response = await client.PutAsJsonAsync("/api/clubs/1/public-images", new Dictionary<string, object?>
        { [changed] = "https://images.example/new.png", ["expectedVersion"] = version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(original, result.GetProperty(preserved).GetString());
        Assert.Equal("https://images.example/new.png", result.GetProperty(changed).GetString());
        var saved = await scope.ServiceProvider.GetRequiredService<ClubDbContext>().Clubs.AsNoTracking().SingleAsync();
        Assert.Equal(changed == "logoUrl" ? original : "https://images.example/new.png", saved.CoverImageUrl);
    }

    [Fact]
    public async Task ClubContract_ExplicitNullClearsOnlySpecifiedImageAndRejectsStaleVersion()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        await using var scope = app.Services.CreateAsyncScope();
        var version = (await scope.ServiceProvider.GetRequiredService<ClubDbContext>().Clubs.FindAsync(1))!.ConcurrencyToken;
        var response = await client.PutAsJsonAsync("/api/clubs/1/public-images", new { logoUrl = (string?)null, expectedVersion = version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("logoUrl").ValueKind);
        Assert.Equal("https://images.example/cover.png", body.GetProperty("coverImageUrl").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/clubs/1/public-images",
            new { coverImageUrl = (string?)null, expectedVersion = version })).StatusCode);
    }

    [Theory]
    [InlineData("{\"expectedVersion\":\"$version\"}")]
    [InlineData("{\"logoUrl\":42,\"expectedVersion\":\"$version\"}")]
    [InlineData("{\"logoUrl\":null,\"expectedVersion\":\"bad\"}")]
    [InlineData("{\"logoUrl\":null,\"coverImageUrl\":null,\"ownerId\":99,\"expectedVersion\":\"$version\"}")]
    [InlineData("{\"logoUrl\":null,\"logoUrl\":null,\"expectedVersion\":\"$version\"}")]
    [InlineData("[]")]
    public async Task ClubContract_ImageRequestRejectsAmbiguousAndInvalidBodies(string json)
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var version = (await db.Clubs.FindAsync(1))!.ConcurrencyToken;
        using var content = new StringContent(json.Replace("$version", version.ToString()), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/clubs/1/public-images", content)).StatusCode);
        Assert.Equal(version, (await db.Clubs.AsNoTracking().SingleAsync()).ConcurrencyToken);
    }

    [Fact]
    public async Task ClubContract_ImageReplayPreservesReceiptAndRechecksOwnershipAndMediaFailureIsAtomic()
    {
        await using var app = await CreateClubManagerAppAsync();
        using var client = CreateAdminContractClient(app, 100, AuthRoles.ClubManager);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var version = (await db.Clubs.FindAsync(1))!.ConcurrencyToken;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/clubs/1/public-images",
            new { logoUrl = "https://images.example/new.png", coverImageUrl = "https://other.invalid/x", expectedVersion = version })).StatusCode);
        Assert.Equal("https://images.example/logo.png", (await db.Clubs.AsNoTracking().SingleAsync()).LogoUrl);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "image-change");
        var request = new { logoUrl = "https://images.example/new.png", expectedVersion = version };
        var first = await client.PutAsJsonAsync("/api/clubs/1/public-images", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();
        var replay = await client.PutAsJsonAsync("/api/clubs/1/public-images", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/clubs/1/public-images",
            new { logoUrl = "https://images.example/new.png", coverImageUrl = (string?)null, expectedVersion = version })).StatusCode);
        (await db.ClubManagerAssignments.SingleAsync(x => x.ManagerUserId == 100)).IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/clubs/1/public-images", request)).StatusCode);
    }
}
