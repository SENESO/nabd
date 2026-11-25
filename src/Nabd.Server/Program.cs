using Microsoft.EntityFrameworkCore;
using Nabd.Core;
using Nabd.Server;
using Microsoft.Data.Sqlite;

using Monitor = Nabd.Core.Monitor;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Nabd") ?? "Data Source=data/nabd.db";
var dataDir = Path.GetDirectoryName(new SqliteConnectionStringBuilder(connectionString).DataSource);
if (!string.IsNullOrEmpty(dataDir))
    Directory.CreateDirectory(dataDir);

builder.Services.AddDbContext<NabdDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddHttpClient<IProbe, HttpProbe>();
builder.Services.AddHostedService<CheckerService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NabdDbContext>();
    db.Database.EnsureCreated();
    // Clean start: probes during a previous life mean nothing about now.
    await db.Monitors.ForEachAsync(m => m.NextCheckAt = DateTime.UtcNow);
    await db.SaveChangesAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.MapGet("/api/monitors", async (NabdDbContext db) =>
{
    var monitors = await db.Monitors.OrderBy(m => m.Id).ToListAsync();
    var since = DateTime.UtcNow.AddHours(-24);
    return monitors.Select(m =>
    {
        var recent = db.CheckResults
            .Where(r => r.MonitorId == m.Id && r.At >= since)
            .OrderByDescending(r => r.At)
            .Take(50)
            .ToList();
        var last = recent.FirstOrDefault();
        return new
        {
            m.Id,
            m.Name,
            m.Url,
            m.IntervalSeconds,
            status = UptimeStats.Status(last),
            uptime24h = UptimeStats.UptimePercent(recent),
            lastLatencyMs = last is null ? (double?)null : Math.Round(last.LatencyMs, 1),
            lastCheckedAt = last?.At,
            sparkline = UptimeStats.Sparkline(recent.AsEnumerable().Reverse().Take(30)),
        };
    });
});

app.MapPost("/api/monitors", async (MonitorInput input, NabdDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Url))
        return Results.BadRequest(new { error = "name and url are required" });
    if (!Uri.TryCreate(input.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "url must be absolute http(s)" });

    var monitor = new Monitor
    {
        Name = input.Name.Trim(),
        Url = input.Url.Trim(),
        IntervalSeconds = Math.Clamp(input.IntervalSeconds ?? 60, 5, 3600),
    };
    db.Monitors.Add(monitor);
    await db.SaveChangesAsync();
    return Results.Created($"/api/monitors/{monitor.Id}", new { monitor.Id });
});

app.MapDelete("/api/monitors/{id}", async (int id, NabdDbContext db) =>
{
    var monitor = await db.Monitors.FindAsync(id);
    if (monitor is null)
        return Results.NotFound(new { error = "no such monitor" });
    db.CheckResults.RemoveRange(db.CheckResults.Where(r => r.MonitorId == id));
    db.Monitors.Remove(monitor);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapGet("/api/monitors/{id}/results", async (int id, int? take, NabdDbContext db) =>
{
    if (!await db.Monitors.AnyAsync(m => m.Id == id))
        return Results.NotFound(new { error = "no such monitor" });
    var results = await db.CheckResults
        .Where(r => r.MonitorId == id)
        .OrderByDescending(r => r.At)
        .Take(Math.Clamp(take ?? 100, 1, 500))
        .ToListAsync();
    return Results.Ok(results);
});

app.MapFallbackToFile("index.html");

app.Run();

public sealed record MonitorInput(string? Name, string? Url, int? IntervalSeconds);

public partial class Program; // for WebApplicationFactory in tests
