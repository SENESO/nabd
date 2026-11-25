using Monitor = Nabd.Core.Monitor;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nabd.Core;

namespace Nabd.Tests;

/// <summary>A probe that never touches the network; each URL gets the outcome the test picked.</summary>
public sealed class FakeProbe : IProbe
{
    public static readonly Dictionary<string, (int? Status, bool Ok)> Outcomes = new();
    public int Calls;

    public Task<CheckResult> ProbeAsync(Monitor monitor, DateTime at, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Calls);
        var (status, ok) = Outcomes.TryGetValue(monitor.Url, out var o) ? o : ((int?)200, true);
        return Task.FromResult(new CheckResult
        {
            MonitorId = monitor.Id, At = at, StatusCode = status, LatencyMs = 12.5, Ok = ok,
            Error = ok ? null : "expected 200",
        });
    }
}

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nabd-tests-{Guid.NewGuid():n}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        FakeProbe.Outcomes.Clear();
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProbe>();
            services.AddSingleton<IProbe, FakeProbe>();
            services.RemoveAll<DbContextOptions<NabdDbContext>>();
            services.AddDbContext<NabdDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        }));
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private async Task<int> AddMonitorAsync(string name = "storefront", string url = "https://shop.example")
    {
        var response = await _http.PostAsJsonAsync("/api/monitors", new { name, url, intervalSeconds = 5 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        return body!["id"];
    }

    [Fact]
    public async Task CreateValidateListAndDelete()
    {
        var id = await AddMonitorAsync();

        var list = await _http.GetFromJsonAsync<List<MonitorView>>("/api/monitors");
        var view = Assert.Single(list!);
        Assert.Equal(id, view.Id);
        Assert.Equal("unknown", view.Status); // no probes yet on a fresh monitor view

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _http.PostAsJsonAsync("/api/monitors", new { name = "x", url = "not-a-url" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _http.PostAsJsonAsync("/api/monitors", new { name = "", url = "https://a.example" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/monitors/{id}")).StatusCode);
        Assert.Empty(await _http.GetFromJsonAsync<List<MonitorView>>("/api/monitors")!);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/monitors/{id}")).StatusCode);
    }

    [Fact]
    public async Task CheckerRecordsResultsAndReschedules()
    {
        var id = await AddMonitorAsync(url: "https://up.example");
        FakeProbe.Outcomes["https://up.example"] = (200, true);

        // The hosted checker ticks every second; give it room to run a few rounds.
        var results = await WaitForAsync(async () =>
            (await _http.GetFromJsonAsync<List<CheckResultView>>($"/api/monitors/{id}/results"))!,
            r => r.Count >= 2, TimeSpan.FromSeconds(15));

        Assert.All(results, r => Assert.True(r.Ok));

        var view = (await _http.GetFromJsonAsync<List<MonitorView>>("/api/monitors"))!.Single();
        Assert.Equal("up", view.Status);
        Assert.Equal(100.0, view.Uptime24h);
        Assert.Equal(12.5, view.LastLatencyMs);
        Assert.NotEmpty(view.Sparkline);
    }

    [Fact]
    public async Task FailingTargetShowsDown()
    {
        var id = await AddMonitorAsync(name: "broken", url: "https://down.example");
        FakeProbe.Outcomes["https://down.example"] = (500, false);

        await WaitForAsync(async () =>
            (await _http.GetFromJsonAsync<List<CheckResultView>>($"/api/monitors/{id}/results"))!,
            r => r.Count >= 1, TimeSpan.FromSeconds(15));

        var view = (await _http.GetFromJsonAsync<List<MonitorView>>("/api/monitors"))!.Single();
        Assert.Equal("down", view.Status);
        Assert.Equal(0.0, view.Uptime24h);
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T>> probe, Func<T, bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var value = await probe();
            if (done(value)) return value;
            await Task.Delay(300);
        }
        throw new TimeoutException("condition never became true");
    }

    public sealed record MonitorView(
        int Id, string Name, string Url, int IntervalSeconds, string Status,
        double? Uptime24h, double? LastLatencyMs, DateTime? LastCheckedAt, List<double> Sparkline);

    public sealed record CheckResultView(
        long Id, int MonitorId, DateTime At, int? StatusCode, double LatencyMs, bool Ok, string? Error);
}
