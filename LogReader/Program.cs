// Program.cs — GET Log Reader (ASP.NET Core). Serves the browser UI from wwwroot and the /api used by it.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using LogReader.Parsing;
using LogReader.Services;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("LogReader").Get<LogReaderOptions>() ?? new LogReaderOptions();
if (string.IsNullOrWhiteSpace(options.LogRoot)) throw new InvalidOperationException("LogReader:LogRoot is required: copy appsettings.Default.json to appsettings.json and fill in the paths.");
if (string.IsNullOrWhiteSpace(options.NpcRoot)) throw new InvalidOperationException("LogReader:NpcRoot is required: copy appsettings.Default.json to appsettings.json and fill in the paths.");

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new EventRegistry(Path.Combine(AppContext.BaseDirectory, "eventRegistry.json")));
builder.Services.AddSingleton<LogStore>();

var app = builder.Build();
var store = app.Services.GetRequiredService<LogStore>();

var sseJson = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(ex, "Unhandled error");
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = ex?.Message ?? "error" });
}));

app.UseDefaultFiles();
app.UseStaticFiles();

// Re-checks (throttled) whether the logs were regenerated; the client compares the stamp across responses.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        store.CheckRun();
        ctx.Response.Headers["X-Run-Stamp"] = store.RunStamp;
    }
    await next();
});

var api = app.MapGroup("/api");

api.MapGet("/config", () => new
{
    calMonths = options.Calendar.Months, calDays = options.Calendar.Days, calHours = options.Calendar.Hours,
    calName = options.Calendar.Name, runStamp = store.RunStamp,
});

api.MapPost("/reset", async () =>
{
    await store.ResetState("manual reset");
    return new { ok = true, npcCount = store.NpcMap.Count, runStamp = store.RunStamp };
});

api.MapGet("/event-registry", () => new { events = store.Registry.Events, idNames = store.Registry.IdNames });

api.MapGet("/world/respawns", () => new { events = store.GetWorldRespawns() });

api.MapGet("/world/map", () =>
{
    var cached = store.Cache.Get<Dictionary<string, object?>>("world-map");
    if (cached != null) return cached;
    var map = store.WorldDb.GetWorldMap()
        ?? new Dictionary<string, object?> { ["locations"] = new List<object>(), ["connections"] = new List<object>(), ["unavailable"] = store.WorldDb.UnavailableReason };
    store.Cache.Set("world-map", map, 6L * 60 * 60 * 1000);
    return map;
});

api.MapGet("/characters", () =>
{
    var result = new Dictionary<string, JsonObject>();
    foreach (var (id, profile) in store.NpcMap)
        result[id] = new JsonObject { ["id"] = id, ["displayName"] = profile.S("displayName") ?? id, ["biology"] = profile.Raw("biology"), ["hasNpcData"] = true };
    foreach (var id in store.ScanLogPersonIds())
        if (!result.ContainsKey(id))
            result[id] = new JsonObject { ["id"] = id, ["displayName"] = $"[{id[..8]}]", ["biology"] = null, ["hasNpcData"] = false };
    var cmp = CultureInfo.CurrentCulture.CompareInfo;
    return result.Values.OrderBy(r => r.S("displayName"), Comparer<string?>.Create((a, b) => cmp.Compare(a, b, CompareOptions.IgnoreCase))).ToList();
});

api.MapGet("/characters/{id}", (string id) =>
{
    id = id.ToLowerInvariant();
    return store.NpcMap.TryGetValue(id, out var p) ? p : new JsonObject { ["id"] = id, ["hasNpcData"] = false };
});

var logs = api.MapGroup("/logs/{id}");
static string Id(string id) => id.ToLowerInvariant();

logs.MapGet("/story", (string id) => new { events = store.BuildStory(Id(id)) });

logs.MapGet("/ticks", (string id) =>
{
    id = Id(id);
    var data = store.Cache.Get<TickData>("ticks:" + id);
    if (data == null)
    {
        data = store.BuildTickStore(id);
        store.Cache.Set("ticks:" + id, data, CharAccumulator.SnapCacheTtlMs);
    }
    return data;
});

logs.MapGet("/summary", (string id) => store.GetSummary(Id(id)));

logs.MapGet("/events", (string id, int? page, int? limit, string? subsystem, string? eventId, string? level, string? from, string? to) =>
{
    var pg = Math.Max(0, page ?? 0);
    var lim = Math.Min(500, Math.Max(1, limit ?? 100));
    int? eid = int.TryParse(eventId, out var n) ? n : null;
    var (items, total, p, l) = store.Files.StreamPage(Id(id), subsystem, eid, level, from, to, pg, lim);
    return new { items, total, page = p, limit = l };
});

logs.MapGet("/memory", (string id) => store.GetMemory(Id(id)));
logs.MapGet("/physiology", (string id) => store.GetPhysio(Id(id)));
logs.MapGet("/psychology", (string id) => store.GetPsych(Id(id)));
logs.MapGet("/behavior", (string id) => store.GetBehavior(Id(id)));

logs.MapGet("/relationships", (string id, string? otherId) =>
{
    var rel = store.GetRel(Id(id));
    if (string.IsNullOrEmpty(otherId)) return Results.Json(rel);
    var pid = otherId.ToLowerInvariant();
    return Results.Json(new
    {
        partners = rel.Partners.Where(p => p.ToLowerInvariant() == pid).ToList(),
        timeline = new Dictionary<string, List<JsonObject>> { [pid] = rel.Timeline.GetValueOrDefault(pid) ?? new() },
    });
});

logs.MapGet("/relationship-pairs", (string id) => new { pairs = RelationshipParsers.ListKnownPairs(store.GetRelEdgesForPair(Id(id)), Id(id)) });

logs.MapGet("/relationship-pair", (string id, string? target) =>
{
    var self = Id(id);
    var tgt = (target ?? "").ToLowerInvariant();
    if (tgt.Length == 0) return Results.Json(new { error = "target query param required" }, statusCode: 400);
    var series = RelationshipParsers.BuildPairSeries(store.GetRelEdgesForPair(self), self, tgt);
    return Results.Json(new { from = self, to = tgt, series, risk = RelationshipParsers.ComputeBreakupRisk(series) });
});

logs.MapGet("/event-frequency", (string id, string? bucket) => store.GetFreq(Id(id), bucket == "day" ? "day" : "hour"));

logs.MapGet("/interactions", (string id, string? partnerId) =>
{
    var data = store.GetInteractions(Id(id));
    if (string.IsNullOrEmpty(partnerId)) return data;
    var pid = partnerId.ToLowerInvariant();
    return new InteractionData
    {
        Interactions = data.Interactions.Where(i => i.S("partnerId") == pid).ToList(),
        ByPartner = new Dictionary<string, List<JsonObject>> { [pid] = data.ByPartner.GetValueOrDefault(pid) ?? new() },
        Stats = data.Stats,
        IntentEvents = data.IntentEvents,
        Utterances = data.Utterances.Where(u => u.S("partnerId") == pid).ToList(),
    };
});

logs.MapGet("/goals", (string id) => store.GetGoals(Id(id)));
logs.MapGet("/locations", (string id) => store.GetLocations(Id(id)));
logs.MapGet("/schedule", (string id) => store.GetSchedule(Id(id)));

logs.MapGet("/snapshot-check", (string id) =>
{
    id = Id(id);
    var snap = new HashSet<int> { 5000, 5100, 5200 };
    var sources = new List<JsonObject>();
    JsonObject Count(string file, string path, bool isGlobal, string? pid)
    {
        var counts = new Dictionary<int, int>();
        store.Files.ForEach(path, isGlobal, pid, e => { if (snap.Contains(e.EventId ?? -1)) counts[e.EventId!.Value] = counts.GetValueOrDefault(e.EventId.Value) + 1; });
        var o = new JsonObject();
        foreach (var (k, v) in counts) o[k.ToString()] = v;
        return new JsonObject { ["file"] = file, ["counts"] = o };
    }
    foreach (var f in store.Files.GetScopedFiles(id)) sources.Add(Count(Path.GetFileName(f.Path), f.Path, false, id));
    if (store.Files.GlobalExists) sources.Add(Count("Characters.jsonl (global)", store.Files.GlobalPath, true, id));

    bool Has(int eid) => sources.Any(s => s.Obj("counts").D(eid.ToString()) > 0);
    var result = new JsonObject
    {
        ["id"] = id, ["sources"] = Js.ToArray(sources),
        ["summary"] = new JsonObject { ["has5000"] = Has(5000), ["has5100"] = Has(5100), ["has5200"] = Has(5200) },
    };
    if (!Has(5000) && store.Files.GlobalExists)
    {
        var raw = new Dictionary<int, int>();
        var samples = new JsonArray();
        store.Files.ForEach(store.Files.GlobalPath, false, null, e =>
        {
            if (!snap.Contains(e.EventId ?? -1)) return;
            raw[e.EventId!.Value] = raw.GetValueOrDefault(e.EventId.Value) + 1;
            if (samples.Count < 3)
                samples.Add(new JsonObject { ["EventId"] = e.EventId, ["PersonId"] = e.PersonId, ["WorldTimeText"] = e.WorldTimeText });
        });
        var rc = new JsonObject();
        foreach (var (k, v) in raw) rc[k.ToString()] = v;
        result["rawGlobalScan"] = new JsonObject { ["countsIgnoringPersonId"] = rc, ["sampleEntries"] = samples };
    }
    return Results.Content(result.ToJsonString(), "application/json");
});

logs.MapGet("/debug", (string id) =>
{
    id = Id(id);
    var files = store.Files.GetLogFiles(id).Select(f => new { path = f.Path, isGlobal = f.IsGlobal }).ToList();
    var all = store.GetEventsRaw(id);
    var counts = new Dictionary<string, int>();
    foreach (var e in all) { var k = e.Get("ev") != null ? e.S("ev")! : "null"; counts[k] = counts.GetValueOrDefault(k) + 1; }
    return new { personId = id, files, totalEvents = all.Count, eventIdCounts = counts };
});

// ── Server-sent events: the work runs on a worker thread and streams through a channel ──
async Task Sse(HttpContext ctx, Action<Action<string, object>, Func<bool>> work)
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    await ctx.Response.Body.FlushAsync();

    var aborted = ctx.RequestAborted;
    var channel = Channel.CreateUnbounded<string>();
    var worker = Task.Run(() =>
    {
        try
        {
            work((ev, data) =>
            {
                if (!aborted.IsCancellationRequested)
                    channel.Writer.TryWrite($"event: {ev}\ndata: {JsonSerializer.Serialize(data, sseJson)}\n\n");
            }, () => aborted.IsCancellationRequested);
        }
        finally { channel.Writer.Complete(); }
    });

    var writable = true;
    await foreach (var msg in channel.Reader.ReadAllAsync())
    {
        if (!writable) continue;
        try { await ctx.Response.WriteAsync(msg); await ctx.Response.Body.FlushAsync(); }
        catch { writable = false; }
    }
    await worker;
}

api.MapGet("/logs/load-all", (HttpContext ctx, string? force) =>
    Sse(ctx, (send, closed) => store.LoadAll(force == "1", send, closed)));

logs.MapGet("/load", (HttpContext ctx, string id) =>
    Sse(ctx, (send, closed) => store.LoadCharacter(Id(id), send, closed)));

app.Logger.LogInformation("GET Log Reader — LogRoot = {Root}, cache TTL = {Ttl} ms", options.LogRoot, options.CacheTtlMs);
app.Run();
