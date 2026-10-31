using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Experience;
using Microsoft.Extensions.Configuration;

namespace Backend.StabilizationTests;

public sealed class ClubExperienceAuthorityClientTests
{
    [Theory]
    [InlineData(null, "/api/internal/experience/clubs/42")]
    [InlineData(7, "/api/internal/experience/clubs/42?userId=7")]
    public async Task GetAsync_OmitsAbsentUserQueryAndPreservesExplicitUser(int? userId, string expectedPath)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://club-authority.test/") };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:InternalWorkflowToken"] = "authority-contract-test-secret"
        }).Build();
        var client = new ClubExperienceAuthorityClient(http, configuration);

        var club = await client.GetAsync(42, userId, CancellationToken.None);

        Assert.NotNull(club);
        Assert.Equal(42, club.ClubId);
        Assert.Equal(expectedPath, handler.PathAndQuery);
        Assert.Equal("authority-contract-test-secret", handler.InternalToken);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }
        public string? InternalToken { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri!.PathAndQuery;
            InternalToken = request.Headers.GetValues("X-Internal-Workflow-Token").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ClubExperienceAuthority(42, "Test club", "HAN", true, false, null))
            });
        }
    }
}
