namespace Nabd.Core;

/// <summary>Rolls a monitor's recent results into what the dashboard shows.</summary>
public static class UptimeStats
{
    /// <summary>Percentage of OK results over the window; null when there is no data yet.</summary>
    public static double? UptimePercent(IReadOnlyCollection<CheckResult> results) =>
        results.Count == 0 ? null : Math.Round(100.0 * results.Count(r => r.Ok) / results.Count, 1);

    /// <summary>The monitor's current health: unknown before the first probe.</summary>
    public static string Status(CheckResult? last) =>
        last is null ? "unknown" : last.Ok ? "up" : "down";

    /// <summary>Recent latencies, oldest first, for the sparkline.</summary>
    public static IReadOnlyList<double> Sparkline(IEnumerable<CheckResult> recentOldestFirst) =>
        recentOldestFirst.Select(r => Math.Round(r.LatencyMs, 1)).ToList();
}
