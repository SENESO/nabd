namespace Nabd.Core;

/// <summary>One watched endpoint: what to probe, how often, and what counts as up.</summary>
public sealed class Monitor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public int ExpectedStatus { get; set; } = 200;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the next probe is due. The scheduler only ever compares this to "now".</summary>
    public DateTime NextCheckAt { get; set; } = DateTime.UtcNow;

    public List<CheckResult> Results { get; set; } = new();

    public static bool IsDue(Monitor monitor, DateTime now) => monitor.NextCheckAt <= now;

    /// <summary>Reschedules from the check's start time, not from "now", so a slow probe can't drift the cadence.</summary>
    public void Reschedule(DateTime checkStartedAt) =>
        NextCheckAt = checkStartedAt + TimeSpan.FromSeconds(IntervalSeconds);
}

/// <summary>The outcome of one probe.</summary>
public sealed class CheckResult
{
    public long Id { get; set; }
    public int MonitorId { get; set; }
    public DateTime At { get; set; }
    public int? StatusCode { get; set; } // null when nothing came back at all
    public double LatencyMs { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
}
