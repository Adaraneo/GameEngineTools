// Services/LogStore.Load.cs — the SSE load operations (global load-all and per-character raw load).
using System.Text.Json;
using System.Text.Json.Nodes;
using LogReader.Parsing;

namespace LogReader.Services;

public sealed partial class LogStore
{
    private static readonly JsonSerializerOptions StateJson = new();

    private static long FileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private void PutState(string id, CharAccumulator acc, long globalOffset, Dictionary<string, long> scopedOffsets, bool globalOnly)
    {
        var dict = acc.ExportState();
        dict["globalOffset"] = globalOffset;
        dict["scopedOffsets"] = scopedOffsets;
        dict["globalOnly"] = globalOnly;
        Index.PutState(id, globalOffset, JsonSerializer.Serialize(dict, StateJson));
    }

    /// <summary>
    /// Reads Characters.jsonl once routing snapshot events to per-character accumulators, then reads each
    /// character's scoped files. Only parsed results are kept (no raw strings), batched to bound memory.
    /// </summary>
    public void LoadAll(bool force, Action<string, object> send, Func<bool> closed)
    {
        try
        {
            NpcReloadInFlight?.Wait();

            var npc = _npcMap;
            var allIds = new HashSet<string>(npc.Keys.Concat(ScanLogPersonIds()));
            var globalOnlyIds = ScanGlobalForPersonIds(allIds);
            foreach (var g in globalOnlyIds) allIds.Add(g);
            if (globalOnlyIds.Count > 0)
                _log.LogInformation("[load-all] Found {Count} additional characters in global Characters.jsonl", globalOnlyIds.Count);

            var charList = allIds.ToList();
            var totalChars = charList.Count;
            send("start", new { totalChars, runStamp = RunStamp });

            var globalPath = Files.GlobalPath;
            var batchSize = Math.Max(1, Opt.LoadBatchSize);
            var knownOffsets = force ? new Dictionary<string, long>() : Index.GetGlobalOffsets();
            var incIds = charList.Where(knownOffsets.ContainsKey).ToList();
            var fullIds = charList.Where(id => !knownOffsets.ContainsKey(id)).ToList();
            var doneCount = 0;

            string NameOf(string id) => npc.TryGetValue(id, out var p) && p.S("displayName") is { } n ? n : id[..8] + "…";

            // ── Phase 1 — incremental characters, grouped by stored global offset so each group shares one tail stream ──
            var groups = new Dictionary<long, List<string>>();
            foreach (var id in incIds)
            {
                var off = knownOffsets[id];
                if (!groups.TryGetValue(off, out var l)) { l = new List<string>(); groups[off] = l; }
                l.Add(id);
            }

            foreach (var (startOff, ids) in groups)
            {
                for (var i = 0; i < ids.Count && !closed(); i += batchSize)
                {
                    var batch = ids.Skip(i).Take(batchSize).ToList();
                    var accs = new Dictionary<string, CharAccumulator>();
                    var newCounts = new Dictionary<string, long>();
                    var oldScoped = new Dictionary<string, Dictionary<string, long>>();
                    var globalOnly = new HashSet<string>();

                    foreach (var id in batch)
                    {
                        var st = Index.GetState(id);
                        if (st == null || (st.B("globalOnly") == true && Files.GetScopedFiles(id).Count > 0)) { fullIds.Add(id); continue; }
                        accs[id] = new CharAccumulator(id, st);
                        oldScoped[id] = st.Obj("scopedOffsets")?.ToDictionary(kv => kv.Key, kv => (long)(Js.NumberOf(kv.Value) ?? 0)) ?? new();
                        newCounts[id] = 0;
                        if (st.B("globalOnly") == true) globalOnly.Add(id);
                    }
                    if (accs.Count == 0) continue;

                    var newGlobalOffset = startOff;
                    if (File.Exists(globalPath))
                    {
                        send("global-start", new { fileName = "Characters.jsonl (tail)", sizeBytes = Math.Max(0, FileSize(globalPath) - startOff) });
                        var r = Files.StreamFrom(globalPath, startOff, entry =>
                        {
                            var pid = entry.PersonLower;
                            if (EventIds.Snapshot.Contains(entry.EventId ?? -1))
                            {
                                if (accs.TryGetValue(pid, out var acc)) { acc.ProcessEntry(entry); newCounts[pid]++; }
                            }
                            else if (globalOnly.Count > 0)
                            {
                                var rpid = entry.RelatedLower;
                                foreach (var gid in globalOnly)
                                    if (gid == pid || gid == rpid) { accs[gid].ProcessEntry(entry); newCounts[gid]++; }
                            }
                        }, lines => send("global-progress", new { linesRead = lines }));
                        newGlobalOffset = r.EndOffset;
                        send("global-done", new { snapshotEventsFound = r.Lines });
                    }

                    foreach (var id in batch)
                    {
                        if (closed()) break;
                        if (!accs.TryGetValue(id, out var acc)) continue;
                        var displayName = NameOf(id);
                        var so = oldScoped[id];
                        var newScoped = new Dictionary<string, long>(so);
                        var scopedFiles = Files.GetScopedFiles(id);

                        send("char-start", new { charIndex = doneCount + 1, totalChars, personId = id, displayName, totalFiles = scopedFiles.Count });

                        for (var fi = 0; fi < scopedFiles.Count && !closed(); fi++)
                        {
                            var filePath = scopedFiles[fi].Path;
                            var fileName = Path.GetFileName(filePath);
                            var startAt = so.GetValueOrDefault(fileName);
                            send("char-file", new
                            {
                                charIndex = doneCount + 1, totalChars, personId = id, displayName, fileIndex = fi + 1,
                                totalFiles = scopedFiles.Count, fileName, sizeBytes = Math.Max(0, FileSize(filePath) - startAt),
                            });
                            var r = Files.StreamFrom(filePath, startAt, entry =>
                            {
                                if (EventIds.Snapshot.Contains(entry.EventId ?? -1)) return;
                                acc.ProcessEntry(entry);
                                newCounts[id]++;
                            }, lines => send("char-progress", new { charIndex = doneCount + 1, totalChars, personId = id, fileName, linesRead = lines }));
                            newScoped[fileName] = r.EndOffset;
                        }

                        var fresh = newCounts[id];
                        var cachesWarm = Cache.Get<SummaryData>("summary:" + id) != null && Cache.Get<PhysioData>("physio-full:" + id) != null;
                        long? totalEvents = null;
                        if (fresh > 0 || !cachesWarm) totalEvents = acc.StoreCaches(Cache);
                        PutState(id, acc, newGlobalOffset, newScoped, globalOnly.Contains(id));
                        accs.Remove(id);
                        send("char-done", new { charIndex = ++doneCount, totalChars, personId = id, totalEvents, cached = fresh == 0, incremental = true });
                    }
                }
            }

            // ── Phase 2 — full read for new / forced / unresumable characters ──
            for (var batchStart = 0; batchStart < fullIds.Count; batchStart += batchSize)
            {
                if (closed()) break;
                var batch = fullIds.Skip(batchStart).Take(batchSize).ToList();
                var accs = batch.ToDictionary(id => id, id => new CharAccumulator(id));

                long batchGlobalOffset = 0;
                if (File.Exists(globalPath))
                {
                    var batchNum = batchStart / batchSize + 1;
                    var totalBatches = (int)Math.Ceiling(fullIds.Count / (double)batchSize);
                    send("global-start", new { fileName = "Characters.jsonl", sizeBytes = FileSize(globalPath), batchNum, totalBatches });

                    long snapCount = 0;
                    var r = Files.StreamFrom(globalPath, 0, entry =>
                    {
                        if (!EventIds.Snapshot.Contains(entry.EventId ?? -1)) return;
                        if (!accs.TryGetValue(entry.PersonLower, out var acc)) return;
                        acc.ProcessEntry(entry);
                        snapCount++;
                    }, lines => send("global-progress", new { linesRead = lines, batchNum }));
                    batchGlobalOffset = r.EndOffset;

                    send("global-done", new { snapshotEventsFound = snapCount, batchNum });
                    if (snapCount == 0 && batchStart == 0)
                        _log.LogWarning("[load-all] No 5000/5100/5200 events found — physiology/psychology/behavior charts will be empty.");
                }

                foreach (var id in batch)
                {
                    if (closed()) break;
                    var acc = accs[id];
                    var scopedFiles = Files.GetScopedFiles(id);
                    var displayName = NameOf(id);
                    var isGlobalOnly = scopedFiles.Count == 0;
                    var scopedOffsets = new Dictionary<string, long>();

                    var filesToRead = !isGlobalOnly ? scopedFiles
                        : File.Exists(globalPath) ? new List<LogFile> { new(globalPath, true) } : new List<LogFile>();

                    send("char-start", new { charIndex = doneCount + 1, totalChars, personId = id, displayName, totalFiles = filesToRead.Count });

                    for (var fi = 0; fi < filesToRead.Count; fi++)
                    {
                        if (closed()) break;
                        var (filePath, isGlobal) = (filesToRead[fi].Path, filesToRead[fi].IsGlobal);
                        var fileName = Path.GetFileName(filePath);
                        send("char-file", new
                        {
                            charIndex = doneCount + 1, totalChars, personId = id, displayName, fileIndex = fi + 1,
                            totalFiles = filesToRead.Count, fileName, sizeBytes = FileSize(filePath),
                        });
                        var r = Files.StreamFrom(filePath, 0, entry =>
                        {
                            if (EventIds.Snapshot.Contains(entry.EventId ?? -1)) return;
                            if (isGlobal && entry.PersonLower != id && entry.RelatedLower != id) return;
                            acc.ProcessEntry(entry);
                        }, lines => send("char-progress", new { charIndex = doneCount + 1, totalChars, personId = id, fileName, linesRead = lines }));
                        if (!isGlobal) scopedOffsets[fileName] = r.EndOffset;
                    }

                    var totalEvents = acc.StoreCaches(Cache);
                    PutState(id, acc, batchGlobalOffset, scopedOffsets, isGlobalOnly);
                    accs.Remove(id);
                    send("char-done", new { charIndex = ++doneCount, totalChars, personId = id, totalEvents, cached = false });
                }
            }

            send("done", new { totalChars });
        }
        catch (Exception ex)
        {
            _log.LogError("[load-all] {Msg}", ex.Message);
            send("error", new { message = ex.Message });
        }
    }

    /// <summary>Per-character raw load: streams all files into the compact events-raw cache with progress.</summary>
    public void LoadCharacter(string id, Action<string, object> send, Func<bool> closed)
    {
        try
        {
            var rawKey = "events-raw:" + id;
            var cached = Cache.Get<List<JsonObject>>(rawKey);
            if (cached != null)
            {
                var sum = Cache.Get<SummaryData>("summary:" + id) ?? new SummaryData { TotalEvents = cached.Count };
                send("done", new { cached = true, totalEvents = sum.TotalEvents, bySubsystem = sum.BySubsystem, byEventId = sum.ByEventId, byLevel = sum.ByLevel, from = sum.From, to = sum.To });
                return;
            }

            var files = Files.GetLogFiles(id);
            send("start", new { totalFiles = files.Count, runStamp = RunStamp });

            var seen = new Dictionary<long, JsonObject>();
            var noId = new List<JsonObject>();
            for (var fi = 0; fi < files.Count; fi++)
            {
                if (closed()) break;
                var (filePath, isGlobal) = (files[fi].Path, files[fi].IsGlobal);
                var fileName = Path.GetFileName(filePath);
                send("file", new { fileIndex = fi + 1, totalFiles = files.Count, fileName, sizeBytes = FileSize(filePath) });
                var entries = Files.Stream(filePath, isGlobal, id, _ => true, e => e.ToCompact(),
                    lines => send("progress", new { fileIndex = fi + 1, totalFiles = files.Count, fileName, linesRead = lines }));
                foreach (var e in entries)
                {
                    if (e.L("i") is { } key) seen.TryAdd(key, e); else noId.Add(e);
                }
            }
            if (closed()) return;

            var all = seen.Values.Concat(noId)
                .OrderBy(e => e.S("rt") ?? "", StringComparer.Ordinal).ThenBy(e => e.L("i") ?? 0).ToList();
            Cache.Set(rawKey, all);
            var summary = Agg.BuildSummaryFromCompact(all);
            Cache.Set("summary:" + id, summary);
            send("done", new { cached = false, totalEvents = summary.TotalEvents, bySubsystem = summary.BySubsystem, byEventId = summary.ByEventId, byLevel = summary.ByLevel, from = summary.From, to = summary.To });
        }
        catch (Exception ex)
        {
            _log.LogError("[load] {Msg}", ex.Message);
            send("error", new { message = ex.Message });
        }
    }
}
