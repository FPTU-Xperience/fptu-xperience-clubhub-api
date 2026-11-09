using System.Net;
using System.Net.Http.Json;
using ClubReportHub.Shared.Experience;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Backend.StabilizationTests;

public sealed class MediaAttachmentClientTests
{
    [Theory]
    [InlineData("https://media.example/verified/image.png", 200)]
    [InlineData("https://media.example/staging/image.png", 503)]
    [InlineData("http://media.example/verified/image.png", 503)]
    [InlineData("", 503)]
    [InlineData(null, 503)]
    public async Task VerificationMustReturnADurableHttpsImage(string? url, int status)
    {
        var client = Create(new Handler((request, _) =>
        {
            Assert.Equal("/api/internal/media/verify", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("internal-test-token", Assert.Single(request.Headers.GetValues("X-Internal-Workflow-Token")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { publicUrl = url }) });
        }));
        var result = await client.VerifyAsync("https://media.example/staging/upload.png", null, "club-cover", 1, Context(), default);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(status == 200 ? url : null, result.PublicUrl);
    }

    [Fact]
    public async Task InternalTimeoutReturnsRetryableUnavailableAndCallerCancellationPropagates()
    {
        var client = Create(new Handler((_, _) => throw new TaskCanceledException("internal timeout")));
        Assert.Equal(503, (await client.VerifyAsync("https://media.example/staging/upload.png", null, "activity-cover", 1, Context(), default)).StatusCode);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.VerifyAsync(
            "https://media.example/staging/upload.png", null, "activity-cover", 1, Context(), cancellation.Token));
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer actor-token";
        return context;
    }

    private static MediaAttachmentClient Create(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://auth.example/") },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Security:InternalWorkflowToken"] = "internal-test-token" }).Build());

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
