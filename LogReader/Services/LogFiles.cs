// Services/LogFiles.cs — JSONL log discovery and streaming (port of logReader.js).
using System.Text;
using LogReader.Parsing;

namespace LogReader.Services;

public sealed record LogFile(string Path, bool IsGlobal);

public sealed class LogFiles
{
    private readonly string _root;
    public LogFiles(LogReaderOptions opt) => _root = opt.LogRoot;

    public string GlobalPath => System.IO.Path.Combine(_root, "Characters.jsonl");
    public bool GlobalExists => File.Exists(GlobalPath);
    public string PersonDir(string personId) => System.IO.Path.Combine(_root, "Person", personId);

    /// <summary>Only the per-person files (never the global log); fast and bounded.</summary>
    public List<LogFile> GetScopedFiles(string personId)
    {
        try
        {
            return Directory.GetFiles(PersonDir(personId), "*.jsonl").Select(f => new LogFile(f, false)).ToList();
        }
        catch { return new List<LogFile>(); }
    }

    /// <summary>Global log plus scoped files; only for callers that can afford to stream the global file.</summary>
    public List<LogFile> GetLogFiles(string personId)
    {
        var files = new List<LogFile>();
        if (GlobalExists) files.Add(new LogFile(GlobalPath, true));
        files.AddRange(GetScopedFiles(personId));
        return files;
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        string? line;
        while ((line = sr.ReadLine()) != null) yield return line;
    }

    private static LogEntry? ParseFiltered(string line, bool isGlobal, string? personId)
    {
        var entry = LogEntry.TryParse(line);
        if (entry == null) return null;
        if (isGlobal && personId != null && entry.PersonLower != personId && entry.RelatedLower != personId) return null;
        return entry;
    }

    /// <summary>Streams a file, passing each entry (optionally filtered by person for the global log).</summary>
    public int ForEach(string path, bool isGlobal, string? personId, Action<LogEntry> onEntry, Action<long>? onProgress = null)
    {
        if (!File.Exists(path)) return 0;
        long count = 0;
        try
        {
            foreach (var line in ReadLines(path))
            {
                count++;
                if (onProgress != null && count % 5000 == 0) onProgress(count);
                var e = ParseFiltered(line, isGlobal, personId);
                if (e != null) onEntry(e);
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        onProgress?.Invoke(count);
        return (int)count;
    }

    public List<T> Stream<T>(string path, bool isGlobal, string? personId, Func<LogEntry, bool> predicate, Func<LogEntry, T> transform, Action<long>? onProgress = null)
    {
        var results = new List<T>();
        ForEach(path, isGlobal, personId, e => { if (predicate(e)) results.Add(transform(e)); }, onProgress);
        return results;
    }

    /// <summary>
    /// Streams complete lines from <paramref name="startOffset"/> and returns the byte offset just past the last
    /// complete line, so a trailing partial line is picked up whole by the next incremental read.
    /// </summary>
    public (long EndOffset, long Lines) StreamFrom(string path, long startOffset, Action<LogEntry> onEntry, Action<long>? onProgress = null)
    {
        long size;
        try { size = new FileInfo(path).Length; } catch { return (startOffset, 0); }
        if (size <= startOffset) return (startOffset, 0);

        long offset = startOffset, lines = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            fs.Seek(startOffset, SeekOrigin.Begin);
            var buf = new byte[1 << 20];
            var carry = new MemoryStream();
            int read;
            while ((read = fs.Read(buf, 0, buf.Length)) > 0)
            {
                var start = 0;
                while (true)
                {
                    var nl = Array.IndexOf(buf, (byte)'\n', start, read - start);
                    if (nl < 0) break;
                    byte[] lineBytes;
                    if (carry.Length > 0)
                    {
                        carry.Write(buf, start, nl - start);
                        lineBytes = carry.ToArray();
                        carry.SetLength(0);
                    }
                    else lineBytes = buf.AsSpan(start, nl - start).ToArray();

                    offset += lineBytes.Length + 1;
                    lines++;
                    if (onProgress != null && lines % 5000 == 0) onProgress(lines);
                    var e = LogEntry.TryParse(Encoding.UTF8.GetString(lineBytes));
                    if (e != null) onEntry(e);
                    start = nl + 1;
                }
                if (start < read) carry.Write(buf, start, read - start);
            }
        }
        catch (FileNotFoundException) { }
        onProgress?.Invoke(lines);
        return (offset, lines);
    }

    /// <summary>All entries of a person from scoped files (global only as a last resort), de-duplicated and time-sorted.</summary>
    public List<T> StreamAllForPerson<T>(string personId, Func<LogEntry, bool> predicate, Func<LogEntry, T> transform, Func<T, (string? Rt, long? Id)> key)
    {
        var files = GetScopedFiles(personId);
        if (files.Count == 0)
            return GlobalExists ? Stream(GlobalPath, true, personId, predicate, transform) : new List<T>();

        var seen = new Dictionary<long, T>();
        var noId = new List<T>();
        var order = new List<T>();
        foreach (var f in files)
            foreach (var item in Stream(f.Path, f.IsGlobal, personId, predicate, transform))
            {
                var (_, id) = key(item);
                if (id is { } i) { if (seen.TryAdd(i, item)) order.Add(item); }
                else order.Add(item);
            }
        _ = noId;
        return order.OrderBy(x => key(x).Rt ?? "", StringComparer.Ordinal).ThenBy(x => key(x).Id ?? 0).ToList();
    }

    /// <summary>Raw entries for a person from scoped files, de-duplicated by EventInstanceId and time-sorted.</summary>
    public List<LogEntry> ReadEntries(string personId, Func<LogEntry, bool> predicate) =>
        StreamAllForPerson(personId, predicate, e => e, e => (e.RealTimestamp, e.EventInstanceId));

    /// <summary>Scoped files plus the global log, de-duplicated by EventInstanceId (snapshot endpoints).</summary>
    public List<LogEntry> ReadSnapshotEntries(string personId, Func<LogEntry, bool> predicate)
    {
        var seen = new HashSet<long>();
        var result = new List<LogEntry>();
        void AddAll(IEnumerable<LogEntry> entries)
        {
            foreach (var e in entries)
                if (e.EventInstanceId is not { } id || seen.Add(id)) result.Add(e);
        }
        AddAll(ReadEntries(personId, predicate));
        if (GlobalExists) AddAll(Stream(GlobalPath, true, personId, predicate, e => e));
        return result;
    }

    /// <summary>Streaming paginator over scoped files only.</summary>
    public (List<System.Text.Json.Nodes.JsonObject> Items, long Total, int Page, int Limit) StreamPage(
        string personId, string? subsystem, int? eventId, string? level, string? from, string? to, int page, int limit)
    {
        const long maxCountScan = 100_000;
        var skip = (long)page * limit;
        var items = new List<System.Text.Json.Nodes.JsonObject>();
        long matched = 0, scannedExtra = 0;
        var done = false;
        var seen = new HashSet<long>();

        bool Matches(LogEntry e) =>
            (string.IsNullOrEmpty(subsystem) || e.Subsystem == subsystem) &&
            (eventId is not { } eid || eid == 0 || e.EventId == eid) &&
            (string.IsNullOrEmpty(level) || e.Level == level) &&
            (string.IsNullOrEmpty(from) || Js.Cmp(e.WorldTimeText, from) >= 0) &&
            (string.IsNullOrEmpty(to) || Js.Cmp(e.WorldTimeText, to) <= 0);

        foreach (var f in GetScopedFiles(personId))
        {
            if (done) break;
            ForEach(f.Path, f.IsGlobal, personId, e =>
            {
                if (done || !Matches(e)) return;
                if (e.EventInstanceId is { } id && !seen.Add(id)) return;
                matched++;
                if (matched > skip && items.Count < limit) items.Add(e.ToCompact());
                else if (items.Count >= limit)
                {
                    scannedExtra++;
                    if (scannedExtra >= maxCountScan) done = true;
                }
            });
        }
        var total = items.Count < limit ? skip + items.Count : skip + limit + scannedExtra;
        return (items, total, page, limit);
    }
}
