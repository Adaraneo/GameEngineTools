// Services/Stores.cs — persistent accumulator index, world.db reader and the event registry.
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LogReader.Services;

/// <summary>
/// Persists per-character accumulator state (plus file byte offsets) so a second load-all reads only appended
/// bytes. Falls back to memory when SQLite cannot be opened.
/// </summary>
public sealed class LogIndex
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, string> _mem = new();
    private string? _connStr;

    public bool IsPersistent => _connStr != null;

    public void Init(string cacheDir, RunSignature current, ILogger log)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var dbPath = Path.Combine(cacheDir, "log-index.db");
            _connStr = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            Exec("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
            Exec("CREATE TABLE IF NOT EXISTS acc_state (person_id TEXT PRIMARY KEY, global_offset INTEGER NOT NULL, state_json TEXT NOT NULL)");

            var stored = Scalar("SELECT value FROM meta WHERE key = 'runSig'") is string s ? RunSignature.FromJson(s) : null;
            if (stored == null || RunSignature.IsNewRun(stored, current))
            {
                Wipe();
                log.LogInformation("[logIndex] Stored state belongs to a different run — wiped.");
            }
            SaveRunSig(current);
            log.LogInformation("[logIndex] SQLite index ready ({Count} persisted character states) at {Path}", Scalar("SELECT COUNT(*) FROM acc_state"), dbPath);
        }
        catch (Exception ex)
        {
            _connStr = null;
            log.LogWarning("[logIndex] SQLite unavailable ({Msg}) — incremental state kept in memory only.", ex.Message);
        }
    }

    private object? Scalar(string sql, params (string, object)[] ps)
    {
        using var c = new SqliteConnection(_connStr);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    private void Exec(string sql, params (string, object)[] ps)
    {
        using var c = new SqliteConnection(_connStr);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    public void SaveRunSig(RunSignature sig)
    {
        if (_connStr == null) return;
        lock (_gate)
            Exec("INSERT INTO meta (key, value) VALUES ('runSig', $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$v", sig.ToJson().ToJsonString()));
    }

    /// <summary>Persisted state JSON for a character, or null.</summary>
    public JsonObject? GetState(string personId)
    {
        string? json;
        if (_connStr != null) { lock (_gate) json = Scalar("SELECT state_json FROM acc_state WHERE person_id = $p", ("$p", personId)) as string; }
        else _mem.TryGetValue(personId, out json);
        return json == null ? null : JsonNode.Parse(json) as JsonObject;
    }

    public void PutState(string personId, long globalOffset, string stateJson)
    {
        if (_connStr != null)
            lock (_gate)
                Exec("INSERT INTO acc_state (person_id, global_offset, state_json) VALUES ($p, $o, $j) ON CONFLICT(person_id) DO UPDATE SET global_offset = excluded.global_offset, state_json = excluded.state_json",
                    ("$p", personId), ("$o", globalOffset), ("$j", stateJson));
        else _mem[personId] = stateJson;
    }

    /// <summary>personId → global-file byte offset (cheap view that avoids loading the state blobs).</summary>
    public Dictionary<string, long> GetGlobalOffsets()
    {
        var m = new Dictionary<string, long>();
        if (_connStr != null)
        {
            lock (_gate)
            {
                using var c = new SqliteConnection(_connStr);
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT person_id, global_offset FROM acc_state";
                using var r = cmd.ExecuteReader();
                while (r.Read()) m[r.GetString(0)] = r.GetInt64(1);
            }
        }
        else
            foreach (var (id, json) in _mem)
                m[id] = (JsonNode.Parse(json) as JsonObject)?["globalOffset"]?.GetValue<long>() ?? 0;
        return m;
    }

    public void Wipe()
    {
        if (_connStr != null) lock (_gate) Exec("DELETE FROM acc_state");
        _mem.Clear();
    }
}

/// <summary>Read-only access to the engine's world.db for the Map tab.</summary>
public sealed class WorldDb
{
    private readonly LogReaderOptions _opt;
    public string? UnavailableReason { get; private set; }
    public WorldDb(LogReaderOptions opt) => _opt = opt;

    public Dictionary<string, object?>? GetWorldMap()
    {
        if (string.IsNullOrEmpty(_opt.WorldDbPath)) { UnavailableReason = "WorldDbPath not configured"; return null; }
        if (!File.Exists(_opt.WorldDbPath)) { UnavailableReason = $"world.db not found at {_opt.WorldDbPath}"; return null; }
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _opt.WorldDbPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            c.Open();
            var locations = new List<Dictionary<string, object?>>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, DisplayName, Type, Region, Terrain, Capacity, X, Y FROM Locations";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    locations.Add(new()
                    {
                        ["id"] = V(r, 0), ["displayName"] = V(r, 1), ["type"] = V(r, 2), ["region"] = V(r, 3),
                        ["terrain"] = V(r, 4), ["capacity"] = V(r, 5), ["x"] = V(r, 6), ["y"] = V(r, 7),
                    });
            }
            var connections = new List<Dictionary<string, object?>>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT FromId, ToId, DistanceMeters FROM Connections";
                using var r = cmd.ExecuteReader();
                while (r.Read()) connections.Add(new() { ["from"] = V(r, 0), ["to"] = V(r, 1), ["distanceMeters"] = V(r, 2) });
            }
            UnavailableReason = null;
            return new() { ["locations"] = locations, ["connections"] = connections };
        }
        catch (Exception ex)
        {
            UnavailableReason = $"world.db could not be opened: {ex.Message}";
            return null;
        }
    }

    private static object? V(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i);
}

/// <summary>The canonical event catalog (eventRegistry.json), generated from CoreLog.cs.</summary>
public sealed class EventRegistry
{
    public JsonArray Events { get; }
    public Dictionary<int, string> IdNames { get; } = new();

    public EventRegistry(string path)
    {
        Events = JsonNode.Parse(File.ReadAllText(path)) as JsonArray ?? new JsonArray();
        foreach (var e in Events)
            if (e is JsonObject o && o["id"] is { } id && o["kind"] is { } kind)
                IdNames[id.GetValue<int>()] = kind.GetValue<string>();
    }

    /// <summary>Fails fast when a hand-curated EventId set references an id absent from the catalog.</summary>
    public void AssertKnown(IEnumerable<int> ids, string label)
    {
        var unknown = ids.Where(i => !IdNames.ContainsKey(i)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"[eventRegistry] {label} references unknown EventId(s): {string.Join(", ", unknown)}");
    }
}
