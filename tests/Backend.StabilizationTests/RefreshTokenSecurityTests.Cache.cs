using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthService.Contracts;

namespace Backend.StabilizationTests;

public sealed partial class RefreshTokenSecurityTests
{
    [Fact]
    public async Task NewLogin_Identity_Rotation_Identity_WorkWithProductionBoundedCache()
    {
        await using var fixture = await RefreshTestFixture.CreateAsync();
        using var client = fixture.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/email-login", new { email = "refresh@fpt.edu.vn", password = EmailPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var initial = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(initial);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", initial.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(initial.RefreshToken!));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await refresh.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);

        // A duplicate refresh inside the grace window receives the winning credentials.
        var duplicate = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(initial.RefreshToken!));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(rotated.RefreshToken, (await duplicate.Content.ReadFromJsonAsync<AuthResponse>())!.RefreshToken);
    }
}
