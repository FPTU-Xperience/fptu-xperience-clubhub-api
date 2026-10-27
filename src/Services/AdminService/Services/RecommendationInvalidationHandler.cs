using System.Text.Json;
using System.Threading.Channels;
using ClubReportHub.Shared.Experience;

namespace AdminService.Services;

public sealed class RecommendationWakeSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public void Wake() => channel.Writer.TryWrite(true);
    public async Task WaitAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(interval);
        try { await channel.Reader.ReadAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}

public sealed class RecommendationInvalidationHandler(RecommendationClient client, RecommendationWakeSignal signal) : IExperienceEventHandler
{
    public bool Handles(string route) => route == RecommendationChangeCapture.Route;
    public async Task ProcessAsync(Guid eventId, string route, JsonElement payload, CancellationToken ct)
    {
        if (!client.Enabled) return;
        var message = payload.Deserialize<RecommendationSourceChanged>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new JsonException("Invalid source event.");
        // Redis acknowledgment follows durable Mongo invalidation. A retry may
        // invalidate twice; it cannot restore an older result or lose the change.
        await client.InvalidateAsync(message.UserIds, ct);
        signal.Wake();
    }
}
