using System.Diagnostics;

namespace Nabd.Core;

/// <summary>Performs one HTTP probe. Abstracted so tests probe fake transports, not the network.</summary>
public interface IProbe
{
    Task<CheckResult> ProbeAsync(Monitor monitor, DateTime at, CancellationToken ct = default);
}

public sealed class HttpProbe : IProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _http;

    public HttpProbe(HttpClient http) => _http = http;

    public async Task<CheckResult> ProbeAsync(Monitor monitor, DateTime at, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var response = await _http.GetAsync(monitor.Url, cts.Token);
            watch.Stop();
            var status = (int)response.StatusCode;
            return new CheckResult
            {
                MonitorId = monitor.Id,
                At = at,
                StatusCode = status,
                LatencyMs = watch.Elapsed.TotalMilliseconds,
                Ok = status == monitor.ExpectedStatus,
                Error = status == monitor.ExpectedStatus ? null : $"expected {monitor.ExpectedStatus}",
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            watch.Stop();
            return new CheckResult
            {
                MonitorId = monitor.Id,
                At = at,
                StatusCode = null,
                LatencyMs = watch.Elapsed.TotalMilliseconds,
                Ok = false,
                Error = e is TaskCanceledException or OperationCanceledException ? "timeout" : "unreachable",
            };
        }
    }
}
