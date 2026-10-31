using System.Net;
using System.Text;
using System.Text.Json;
using AdminService.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class RecommendationClientTests
{
    [Theory]
    [InlineData("feed")]
    [InlineData("invalidate")]
    public async Task MutationSendsKnownUtf8LengthBeforeTransportReadsBody(string operation)
    {
        var handler = new CaptureHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Recommendation:Token"] = new string('x', 64) }).Build();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://recommendation.test/") };
        var client = new RecommendationClient(http, config);
        if (operation == "feed")
            await client.FeedAsync(new { schemaVersion = "1.0", label = "Hoạt động mô phỏng" }, DateTimeOffset.UnixEpoch, default);
        else
            await client.InvalidateAsync([2008, 2009], default);
        Assert.NotNull(handler.ContentLength);
        Assert.Equal(Encoding.UTF8.GetByteCount(handler.Body!), handler.ContentLength);
        Assert.Equal("application/json", handler.MediaType);
        Assert.Equal(new string('x', 64), handler.Token);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.True(json.RootElement.TryGetProperty(operation == "feed" ? "snapshot" : "userIds", out _));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public long? ContentLength { get; private set; }
        public string? Body { get; private set; }
        public string? MediaType { get; private set; }
        public string? Token { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ContentLength = request.Content!.Headers.ContentLength;
            MediaType = request.Content.Headers.ContentType!.MediaType;
            Token = request.Headers.GetValues("X-Recommendation-Token").Single();
            Body = await request.Content.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
