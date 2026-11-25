using Monitor = Nabd.Core.Monitor;
using System.Net;
using Nabd.Core;

namespace Nabd.Tests;

public class CoreTests
{
    [Fact]
    public void DueOnlyWhenNextCheckHasPassed()
    {
        var now = DateTime.UtcNow;
        var monitor = new Monitor { Name = "a", Url = "https://a.example", NextCheckAt = now.AddSeconds(5) };
        Assert.False(Monitor.IsDue(monitor, now));
        Assert.True(Monitor.IsDue(monitor, now.AddSeconds(6)));
    }

    [Fact]
    public void RescheduleAnchorsToCheckStartNotCompletion()
    {
        var monitor = new Monitor { Name = "a", Url = "https://a.example", IntervalSeconds = 60 };
        var started = DateTime.UtcNow;
        monitor.Reschedule(started);
        Assert.Equal(started.AddSeconds(60), monitor.NextCheckAt);
    }

    [Fact]
    public void UptimePercentRoundsAndHandlesEmpty()
    {
        Assert.Null(UptimeStats.UptimePercent(Array.Empty<CheckResult>()));
        var results = new[]
        {
            new CheckResult { Ok = true }, new CheckResult { Ok = true }, new CheckResult { Ok = false },
        };
        Assert.Equal(66.7, UptimeStats.UptimePercent(results));
    }

    [Fact]
    public void StatusNeedsAFirstProbe()
    {
        Assert.Equal("unknown", UptimeStats.Status(null));
        Assert.Equal("up", UptimeStats.Status(new CheckResult { Ok = true }));
        Assert.Equal("down", UptimeStats.Status(new CheckResult { Ok = false }));
    }
}

public class ProbeTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }

    private static readonly Monitor Monitor = new() { Name = "site", Url = "https://site.example", ExpectedStatus = 200 };

    [Fact]
    public async Task MatchingStatusIsUp()
    {
        var probe = new HttpProbe(new HttpClient(new StubHandler(HttpStatusCode.OK)));
        var result = await probe.ProbeAsync(Monitor, DateTime.UtcNow);
        Assert.True(result.Ok);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task WrongStatusIsDownWithReason()
    {
        var probe = new HttpProbe(new HttpClient(new StubHandler(HttpStatusCode.InternalServerError)));
        var result = await probe.ProbeAsync(Monitor, DateTime.UtcNow);
        Assert.False(result.Ok);
        Assert.Equal(500, result.StatusCode);
        Assert.Equal("expected 200", result.Error);
    }

    [Fact]
    public async Task UnreachableIsDownWithoutStatus()
    {
        var probe = new HttpProbe(new HttpClient(new FailingHandler()));
        var result = await probe.ProbeAsync(Monitor, DateTime.UtcNow);
        Assert.False(result.Ok);
        Assert.Null(result.StatusCode);
        Assert.Equal("unreachable", result.Error);
    }
}
