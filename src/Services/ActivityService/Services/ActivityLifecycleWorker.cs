namespace ActivityService.Services;

public sealed class ActivityLifecycleWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<ActivityLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First pass catches downtime/backlog; later passes need no user requests.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ActivityLifecycleSynchronizer>()
                    .SynchronizeAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Includes concurrent decisions/deadlocks. The next pass rereads current data.
                logger.LogError(ex, "Activity lifecycle synchronization failed; will retry on the next pass.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
