using Microsoft.EntityFrameworkCore;
using Nabd.Core;

using Monitor = Nabd.Core.Monitor;

namespace Nabd.Server;

/// <summary>
/// The heartbeat: every second, find due monitors and probe them in
/// parallel. Each probe writes one CheckResult and reschedules the monitor
/// from the probe's start time, so cadence survives slow targets.
/// </summary>
public sealed class CheckerService(
    IServiceScopeFactory scopes,
    IProbe probe,
    ILogger<CheckerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueChecksAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "check round failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    internal async Task<int> RunDueChecksAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NabdDbContext>();

        var now = DateTime.UtcNow;
        var due = await db.Monitors.Where(m => m.NextCheckAt <= now).ToListAsync(ct);
        if (due.Count == 0)
            return 0;

        await Task.WhenAll(due.Select(m => CheckOneAsync(m, now, ct)));

        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private async Task CheckOneAsync(Monitor monitor, DateTime startedAt, CancellationToken ct)
    {
        var result = await probe.ProbeAsync(monitor, startedAt, ct);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NabdDbContext>();
        db.CheckResults.Add(result);
        var tracked = await db.Monitors.FindAsync(new object[] { monitor.Id }, ct);
        tracked?.Reschedule(startedAt);
        await db.SaveChangesAsync(ct);
    }
}
