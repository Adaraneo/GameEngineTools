// Services/Infrastructure.cs — options, TTL cache and run-signature detection.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace LogReader.Services;

/// <summary>Bound from the "LogReader" section of appsettings.json.</summary>
public sealed class LogReaderOptions
{
    /// <summary>Root of the log directory (contains Person/ and/or Characters.jsonl).</summary>
    public string LogRoot { get; set; } = "";

    /// <summary>Directory scanned recursively for NPC JSON files.</summary>
    public string NpcRoot { get; set; } = "";

    /// <summary>Optional engine world.db for the Map tab.</summary>
    public string WorldDbPath { get; set; } = "";

    public int CacheTtlMs { get; set; } = 300_000;

    /// <summary>Characters processed per batch during load-all.</summary>
    public int LoadBatchSize { get; set; } = 10;

    public CalendarOptions Calendar { get; set; } = new();
}

public sealed class CalendarOptions
{
    public int Months { get; set; } = 10;
    public int Days { get; set; } = 36;
    public int Hours { get; set; } = 26;
    public string Name { get; set; } = "Vigilia Insectianis";
}

/// <summary>Thread-safe cache with per-key TTL.</summary>
public sealed class TtlCache
{
    private readonly ConcurrentDictionary<string, (object Data, long Expires)> _map = new();
    private readonly long _ttlMs;

    public TtlCache(long ttlMs) => _ttlMs = ttlMs;

    public T? Get<T>(string key) where T : class
    {
        if (!_map.TryGetValue(key, out var e)) return null;
        if (Environment.TickCount64 > e.Expires) { _map.TryRemove(key, out _); return null; }
        return e.Data as T;
    }

    public void Set(string key, object data, long? ttlOverrideMs = null) =>
        _map[key] = (data, Environment.TickCount64 + (ttlOverrideMs ?? _ttlMs));

    public void Clear() => _map.Clear();
}

/// <summary>Cheap signature of the log root used to notice a regenerated run.</summary>
public sealed record RunSignature(string Kind, long Size, string? Head)
{
    public static RunSignature Compute(string logRoot)
    {
        var global = Path.Combine(logRoot, "Characters.jsonl");
        if (File.Exists(global))
        {
            long size = 0;
            try { size = new FileInfo(global).Length; } catch { }
            return new RunSignature("global", size, ReadHead(global));
        }
        string[] names = Array.Empty<string>();
        try { names = Directory.GetDirectories(Path.Combine(logRoot, "Person")).Select(d => Path.GetFileName(d)!).OrderBy(n => n, StringComparer.Ordinal).ToArray(); } catch { }
        return new RunSignature("dirs", names.Length, string.Join(",", names));
    }

    private static string? ReadHead(string path, int bytes = 256)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[bytes];
            var n = fs.Read(buf, 0, bytes);
            return Encoding.UTF8.GetString(buf, 0, n);
        }
        catch { return null; }
    }

    /// <summary>True when <paramref name="next"/> shows the logs were regenerated since <paramref name="prev"/>.</summary>
    public static bool IsNewRun(RunSignature? prev, RunSignature next) =>
        prev != null && (prev.Kind != next.Kind || prev.Head != next.Head || next.Size < prev.Size);

    public JsonObject ToJson() => new() { ["kind"] = Kind, ["size"] = Size, ["head"] = Head };

    public static RunSignature? FromJson(string json)
    {
        try
        {
            var o = JsonNode.Parse(json) as JsonObject;
            return o == null ? null : new RunSignature(o["kind"]?.GetValue<string>() ?? "", o["size"]?.GetValue<long>() ?? 0, o["head"]?.GetValue<string>());
        }
        catch { return null; }
    }
}
