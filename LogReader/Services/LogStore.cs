// Services/LogStore.cs — shared state and the cold-path data builders behind the API
// (port of the request-time logic in server.js).
using System.Text.Json.Nodes;
using LogReader.Parsing;
using static LogReader.Services.Agg;

namespace LogReader.Services;

public sealed partial class LogStore
{
    private const long SnapTtl = CharAccumulator.SnapCacheTtlMs;

    public LogReaderOptions Opt { get; }
    public TtlCache Cache { get; }
    public LogFiles Files { get; }
    public LogIndex Index { get; } = new();
    public WorldDb WorldDb { get; }
    public EventRegistry Registry { get; }
    private readonly ILogger<LogStore> _log;

    private volatile Dictionary<string, JsonObject> _npcMap = new();
    private Task? _npcReload;
    private readonly object _npcGate = new();

    private RunSignature _runSig;
    private long _lastRunCheck;
    public string RunStamp { get; private set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x");

    public LogStore(LogReaderOptions opt, EventRegistry registry, IHostEnvironment env, ILogger<LogStore> log)
    {
        Opt = opt; Registry = registry; _log = log;
        Cache = new TtlCache(opt.CacheTtlMs);
        Files = new LogFiles(opt);
        WorldDb = new WorldDb(opt);

        foreach (var (label, set) in EventIds.All()) registry.AssertKnown(set, label);

        _runSig = RunSignature.Compute(opt.LogRoot);
        Index.Init(Path.Combine(env.ContentRootPath, ".cache"), _runSig, log);
        ReloadNpcMap();
    }

    public IReadOnlyDictionary<string, JsonObject> NpcMap => _npcMap;

    /// <summary>Single-flight NPC map (re)load; concurrent triggers share one pass.</summary>
    public Task ReloadNpcMap()
    {
        lock (_npcGate)
        {
            _npcReload ??= Task.Run(() =>
            {
                try
                {
                    _npcMap = NpcLoader.LoadAll(Opt.NpcRoot, _log);
                    _log.LogInformation("[server] NPC map ready: {Count} profiles", _npcMap.Count);
                }
                catch (Exception ex) { _log.LogError("[server] Failed to load NPC map: {Msg}", ex.Message); }
                finally { lock (_npcGate) _npcReload = null; }
            });
            return _npcReload;
        }
    }

    public Task? NpcReloadInFlight { get { lock (_npcGate) return _npcReload; } }

    /// <summary>Throttled check whether the logs were regenerated; flushes state when they were.</summary>
    public void CheckRun()
    {
        var now = Environment.TickCount64;
        if (now - _lastRunCheck <= 3000) return;
        _lastRunCheck = now;
        var sig = RunSignature.Compute(Opt.LogRoot);
        if (RunSignature.IsNewRun(_runSig, sig)) ResetState("log regeneration detected");
        else _runSig = sig;
    }

    public Task ResetState(string reason)
    {
        Cache.Clear();
        Index.Wipe();
        _runSig = RunSignature.Compute(Opt.LogRoot);
        RunStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x");
        Index.SaveRunSig(_runSig);
        _log.LogInformation("[server] Caches flushed ({Reason}); run-stamp {Stamp}", reason, RunStamp);
        return ReloadNpcMap();
    }

    public List<string> ScanLogPersonIds()
    {
        try
        {
            return Directory.GetDirectories(Path.Combine(Opt.LogRoot, "Person"))
                .Select(d => Path.GetFileName(d)!).Where(NpcLoader.IsUuid).Select(n => n.ToLowerInvariant()).ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Characters that appear only in the global log (no scoped directory).</summary>
    public HashSet<string> ScanGlobalForPersonIds(ISet<string> known)
    {
        var found = new HashSet<string>();
        if (!Files.GlobalExists) return found;
        Files.ForEach(Files.GlobalPath, false, null, e =>
        {
            var pid = e.PersonLower;
            if (pid.Length > 0 && NpcLoader.IsUuid(pid) && !known.Contains(pid)) found.Add(pid);
        });
        return found;
    }

    // ── Raw entry access ─────────────────────────────────────────────────────────────

    /// <summary>Entries for a person: from the compact events-raw cache when warm, else streamed from scoped files.</summary>
    public List<LogEntry> GetRawEntries(string id, Func<LogEntry, bool> predicate)
    {
        var cached = Cache.Get<List<JsonObject>>("events-raw:" + id);
        if (cached != null)
            return cached.Select(LogEntry.FromCompact).Where(predicate).ToList();
        return Files.ReadEntries(id, predicate);
    }

    public List<JsonObject> GetEventsRaw(string id)
    {
        var key = "events-raw:" + id;
        var all = Cache.Get<List<JsonObject>>(key);
        if (all == null)
        {
            all = Files.StreamAllForPerson(id, _ => true, e => e.ToCompact(), o => (o.S("rt"), o.L("i")));
            Cache.Set(key, all);
        }
        return all;
    }

    // ── Per-endpoint data (cache first, then rebuild from the logs) ─────────────────────

    public SummaryData GetSummary(string id)
    {
        var s = Cache.Get<SummaryData>("summary:" + id);
        if (s != null) return s;
        s = BuildSummaryFromCompact(GetEventsRaw(id));
        Cache.Set("summary:" + id, s);
        return s;
    }

    public FreqData GetFreq(string id, string bucket)
    {
        var key = "freq:" + id + ":" + bucket;
        var f = Cache.Get<FreqData>(key);
        if (f != null) return f;
        f = BuildEventFrequency(GetEventsRaw(id), bucket);
        Cache.Set(key, f);
        return f;
    }

    public MemoryData GetMemory(string id)
    {
        var d = Cache.Get<MemoryData>("memory:" + id);
        if (d != null) return d;
        var raw = GetRawEntries(id, e => e.EventId is 3000 or 3001 or 3002 or 3003);
        List<JsonObject> Of(int eid, Func<LogEntry, JsonObject?> parse) => raw.Where(e => e.EventId == eid).Select(parse).OfType<JsonObject>().ToList();
        d = new MemoryData
        {
            Encoded = Of(3000, MemoryParsers.ParseEncoded), Consolidated = Of(3001, MemoryParsers.ParseConsolidated),
            Reconsolidated = Of(3002, MemoryParsers.ParseReconsolidated), BeliefUpdates = Of(3003, MemoryParsers.ParseBeliefUpdated),
        };
        Cache.Set("memory:" + id, d);
        return d;
    }

    public PhysioData GetPhysio(string id)
    {
        var d = Cache.Get<PhysioData>("physio-full:" + id);
        if (d != null) return d;
        var cold = new HashSet<int>(EventIds.Physio.Concat(EventIds.Obj));
        var raw = Files.ReadSnapshotEntries(id, e => cold.Contains(e.EventId ?? -1));
        d = new PhysioData();
        foreach (var e in raw)
        {
            var eid = e.EventId ?? -1;
            switch (eid)
            {
                case 5000: Add(SnapshotParsers.ParsePhysiology(e), d.Snapshots); break;
                case 5001: Add(SleepParsers.ParsePhysioCycle(e), d.Cycle); break;
                case 5002:
                    if (SleepParsers.ParsePhysioSleepEnded(e) is { } se)
                    {
                        se["eventId"] = 5002; se["label"] = "Sleep Ended"; se["icon"] = "💤"; se["level"] = "info";
                        d.SleepEvents.Add(se);
                    }
                    break;
                case 1003: Add(BehaviorParsers.ParseAction1003(e), d.Actions); break;
                case 5003 or 5004 or 5005 or 5006: Add(SnapshotParsers.ParseReproEvent(e), d.ReproEvents); break;
                case 5007: Add(SnapshotParsers.ParseNaturalDeath(e), d.SleepEvents); break;
                case 5600: Add(SnapshotParsers.ParseHormoneSnapshot(e), d.Hormones); break;
                case 1500: Add(ObjectParsers.ParseObjectUsed(e), d.ObjectUsed); break;
                case 1501: Add(ObjectParsers.ParseObjectRespawned(e), d.ObjectRespawned); break;
                default:
                    if (eid >= 1100 && eid <= 1113) Add(SleepParsers.ParseSleepEvent(e), d.SleepEvents);
                    break;
            }
        }
        Cache.Set("physio-full:" + id, d);
        return d;
    }

    private static readonly HashSet<int> PsychCold = new() { 5100, 5101, 5102, 5103, 5104, 5300, 1600, 1601, 5400, 1610, 5500, 5210, 5700 };

    public PsychData GetPsych(string id)
    {
        var d = Cache.Get<PsychData>("psych-full:" + id);
        if (d != null) return d;
        var raw = Files.ReadSnapshotEntries(id, e => PsychCold.Contains(e.EventId ?? -1));
        d = new PsychData();
        foreach (var e in raw)
        {
            switch (e.EventId)
            {
                case 5100: Add(SnapshotParsers.ParsePsychology(e), d.Snapshots); break;
                case 5103: Add(SnapshotParsers.ParseEmotionTransition(e), d.PsychEvents); break;
                case 5104: Add(SnapshotParsers.ParseMoodBaselineShifted(e), d.PsychEvents); break;
                case 5300: Add(SnapshotParsers.ParseSelfConcept(e), d.SelfConcept); break;
                case 5400: Add(SnapshotParsers.ParseInterests(e), d.Interests); break;
                case 5500: Add(SnapshotParsers.ParseDarkCoreSnapshot(e), d.DarkCoreSnapshots); break;
                case 5210: Add(SnapshotParsers.ParseNeedAppraisal(e), d.NeedAppraisal); break;
                case 5700: Add(SnapshotParsers.ParseValuesSnapshot(e), d.ValuesSnapshots); break;
                case 1610: Add(MemoryParsers.ParseMutualKnowledge(e), d.PsychEvents); break;
                case 1600 or 1601: Add(SnapshotParsers.ParseLifeStageEvent(e), d.PsychEvents); break;
                default: Add(SnapshotParsers.ParsePsychEvent(e), d.PsychEvents); break;
            }
        }
        Cache.Set("psych-full:" + id, d, SnapTtl);
        return d;
    }

    public BehaviorData GetBehavior(string id)
    {
        var d = Cache.Get<BehaviorData>("behav-full:" + id);
        if (d == null)
        {
            var cold = new HashSet<int>(EventIds.Behav.Concat(EventIds.Values).Concat(EventIds.DarkCoreMod).Concat(EventIds.Travel));
            var raw = Files.ReadSnapshotEntries(id, e => cold.Contains(e.EventId ?? -1));
            d = new BehaviorData();
            foreach (var e in raw)
            {
                switch (e.EventId)
                {
                    case 5200: Add(SnapshotParsers.ParseBehavior(e), d.Snapshots); break;
                    case 1000: Add(BehaviorParsers.ParseCandidate(e), d.Candidates); break;
                    case 1001: Add(BehaviorParsers.ParseDecision(e), d.Decisions); break;
                    case 1002: Add(BehaviorParsers.ParseRunning(e), d.Running); break;
                    case 1003: Add(BehaviorParsers.ParseAction1003(e), d.Actions); break;
                    case 1004: Add(BehaviorParsers.ParseCooldownSet(e), d.Cooldowns); break;
                    case 1011: Add(BehaviorParsers.ParseHabit(e), d.Habits); break;
                    case 1012: Add(BehaviorParsers.ParseHabitDecayed(e), d.Habits); break;
                    case 1013: Add(BehaviorParsers.ParseHabitPruned(e), d.Habits); break;
                    case 1014: Add(BehaviorParsers.ParseHabitBias(e), d.Habits); break;
                    case 5201: Add(BehaviorParsers.ParsePlanDetail(e), d.Plans); break;
                    case 1017: Add(BehaviorParsers.ParseDarkCoreModifier(e), d.DarkCoreEvents); break;
                    case 1210: Add(ValuesParsers.ParseProfile(e), d.ValuesEvents); break;
                    case 1211: Add(ValuesParsers.ParseCongruenceViolation(e), d.ValuesEvents); break;
                    case 1212: Add(ValuesParsers.ParseGuiltSpike(e), d.ValuesEvents); break;
                    case 1700 or 1701: Add(MovementParsers.ParseTravelEvent(e), d.TravelEvents); break;
                }
            }
            Cache.Set("behav-full:" + id, d);
        }
        if (d.Intents == null && Cache.Get<InteractionData>("interactions:" + id) is { } intData)
            d.Intents = intData.IntentEvents;
        return d;
    }

    public RelData GetRel(string id)
    {
        var d = Cache.Get<RelData>("rel:" + id);
        if (d != null) return d;
        var rawRel = GetRawEntries(id, e => IsRelLogEntry(e.EventId ?? -1));
        var edges = rawRel.Where(e => e.EventId == 2005).Select(RelationshipParsers.ParseEdge).OfType<JsonObject>()
            .Where(e => (e.S("from") ?? "").ToLowerInvariant() == id).ToList();
        var applies = rawRel.Where(e => e.EventId == 2002 && e.PersonLower == id).Select(RelationshipParsers.ParseApply).OfType<JsonObject>().ToList();
        var logEvts = rawRel.Select(e => RelLogRecordFor(e, id)).OfType<JsonObject>();
        d = BuildRelData(edges, applies, logEvts);
        Cache.Set("rel:" + id, d);
        return d;
    }

    public List<JsonObject> GetRelEdgesForPair(string id)
    {
        var edges = Cache.Get<List<JsonObject>>("rel-edges:" + id);
        if (edges != null) return edges;
        var raw = GetRawEntries(id, e => e.EventId == 2005);
        edges = raw.Select(RelationshipParsers.ParseEdge).OfType<JsonObject>().Where(e => e.S("from") == id).ToList();
        Cache.Set("rel-edges:" + id, edges);
        return edges;
    }

    private static readonly HashSet<int> InterCold = new() { 1200, 1201, 1202, 1203, 1207, 1208, 1209, 1005, 1006, 1007, 1008, 1009, 2001, 2002, 1220 };

    public InteractionData GetInteractions(string id)
    {
        var d = Cache.Get<InteractionData>("interactions:" + id);
        if (d != null) return d;
        var raw = GetRawEntries(id, e => InterCold.Contains(e.EventId ?? -1));
        List<JsonObject> i1201 = new(), i1202 = new(), i1203 = new(), ctxs = new(), intents = new(), rel2001 = new(), rel2002 = new(), utter = new();
        foreach (var e in raw)
        {
            var eid = e.EventId ?? -1;
            switch (eid)
            {
                case 1200: Add(InteractionParsers.ParseContext(e), ctxs); break;
                case 1201: Add(InteractionParsers.ParseDecision(e), i1201); break;
                case 1202: Add(InteractionParsers.ParseTouchDecision(e), i1202); break;
                case 1203: Add(InteractionParsers.ParseSexualProposal(e), i1203); break;
                case 1207 or 1208 or 1209: Add(InteractionParsers.ParseNormViolation(e), rel2001); break;
                case 10003:
                    if (InteractionParsers.ParseReachOutRouted(e) is { } ro && ro.S("partnerId") is { Length: > 0 } rp && rp != id) rel2001.Add(ro);
                    break;
                case 2001:
                    if (RelationshipParsers.ParseRelEventForLog(e) is { } r1 && r1.S("partnerId") is { Length: > 0 } p1 && p1 != id) rel2001.Add(r1);
                    break;
                case 2002:
                    if (RelationshipParsers.ParseRelEventForLog(e) is { } r2 && r2.S("partnerId") is { Length: > 0 } p2 && p2 != id) rel2002.Add(r2);
                    break;
                case 1220: Add(DialogueParsers.ParseSpeechActUttered(e), utter); break;
                default:
                    if (eid >= 1005 && eid <= 1009) Add(GoalParsers.ParseIntentEvent(e), intents);
                    break;
            }
        }
        d = BuildInteractionData(i1201, i1202, i1203, ctxs, rel2001.Concat(rel2002).ToList(), id);
        d.IntentEvents = intents.OrderBy(x => x.S("worldTime") ?? "", StringComparer.Ordinal).ToList();
        d.Utterances = utter.OrderBy(x => x.S("worldTime") ?? "", StringComparer.Ordinal).ToList();
        Cache.Set("interactions:" + id, d);
        return d;
    }

    public GoalData GetGoals(string id)
    {
        var d = Cache.Get<GoalData>("goals:" + id);
        if (d != null) return d;
        var evts = GetRawEntries(id, e => EventIds.Goal.Contains(e.EventId ?? -1)).Select(GoalParsers.ParseGoalEvent).OfType<JsonObject>().ToList();
        d = new GoalData { Events = evts, Timelines = GoalParsers.BuildTimelines(evts), ActiveGoals = BuildActiveGoals(evts) };
        Cache.Set("goals:" + id, d);
        return d;
    }

    public LocationData GetLocations(string id)
    {
        var d = Cache.Get<LocationData>("locations:" + id);
        if (d != null) return d;
        var raw = GetRawEntries(id, e => EventIds.Location.Contains(e.EventId ?? -1) || e.EventId == 1003);
        List<JsonObject> ctxs = new(), moves = new(), objs = new(), actions = new();
        foreach (var e in raw)
        {
            switch (e.EventId)
            {
                case 1200: Add(InteractionParsers.ParseContext(e), ctxs); break;
                case 1205: Add(InteractionParsers.ParseObjectInteraction(e), objs); break;
                case 1206: Add(InteractionParsers.ParseMoveAction(e), moves); break;
                case 1003: Add(BehaviorParsers.ParseAction1003(e), actions); break;
            }
        }
        var (sessions, byLocation) = InteractionParsers.BuildLocationSessions(ctxs, moves, moves.Concat(objs).Concat(actions).ToList());
        d = new LocationData { Sessions = sessions, ByLocation = byLocation, ContextEvents = ctxs, ObjectInteractions = objs };
        Cache.Set("locations:" + id, d);
        return d;
    }

    public ScheduleData GetSchedule(string id)
    {
        var d = Cache.Get<ScheduleData>("schedule:" + id);
        if (d != null) return d;
        var evts = GetRawEntries(id, e => EventIds.Schedule.Contains(e.EventId ?? -1)).Select(ScheduleParsers.ParseScheduleEvent).OfType<JsonObject>().ToList();
        d = new ScheduleData { Events = evts };
        Cache.Set("schedule:" + id, d);
        return d;
    }

    public List<JsonObject> GetWorldRespawns()
    {
        var cached = Cache.Get<List<JsonObject>>("world-respawns");
        if (cached != null) return cached;
        var events = new List<JsonObject>();
        if (Files.GlobalExists)
            Files.ForEach(Files.GlobalPath, false, null, e =>
            {
                if (e.EventId == 1501 && ObjectParsers.ParseObjectRespawned(e) is { } p) events.Add(p);
            });
        Cache.Set("world-respawns", events, SnapTtl);
        return events;
    }

    private static void Add(JsonObject? p, List<JsonObject> list) { if (p != null) list.Add(p); }

    // ── Story and tick store ─────────────────────────────────────────────────────────

    private static JsonObject With(JsonObject ev, string subsystem)
    {
        var o = ev.Clone();
        o["subsystem"] = subsystem;
        return o;
    }

    public List<JsonObject> BuildStory(string id)
    {
        var physio = Cache.Get<PhysioData>("physio-full:" + id);
        var behav = Cache.Get<BehaviorData>("behav-full:" + id);
        var goals = Cache.Get<GoalData>("goals:" + id);
        var rel = Cache.Get<RelData>("rel:" + id);
        var mem = Cache.Get<MemoryData>("memory:" + id);

        var events = new List<JsonObject>();
        void Push(JsonObject ev) { if (!string.IsNullOrEmpty(ev.S("worldTime"))) events.Add(ev); }

        foreach (var ev in physio?.SleepEvents ?? new())
            if ((int?)ev.D("eventId") is 1101 or 1102 or 1105 or 1106 or 1108) Push(With(ev, "sleep"));

        var skip = new HashSet<string> { "Idle", "MoveToRest" };
        var social = new HashSet<string> { "ReachOut", "InviteIntimacy", "Create", "Work" };
        foreach (var ev in behav?.Actions ?? new())
        {
            var action = ev.S("action");
            if (action != null && skip.Contains(action)) continue;
            Push(With(ev, action != null && social.Contains(action) ? "behavior-action" : "physio-action"));
        }
        foreach (var ev in goals?.Events ?? new())
            if (ev.S("type") is "activated" or "resolved" or "seeded") Push(With(ev, "goal"));
        foreach (var list in rel?.RelEvents.Values ?? Enumerable.Empty<List<JsonObject>>())
            foreach (var ev in list)
            {
                var kind = ev.S("kind");
                if (kind is "event" or "first_impression" || (kind == "apply" && ev.Obj("changes") is { Count: > 0 }))
                    Push(With(ev, "relationship"));
            }
        foreach (var ev in mem?.Encoded ?? new())
            if ((ev.D("salience") ?? 0) >= 0.6) Push(With(ev, "memory"));
        foreach (var ev in physio?.SleepEvents ?? new())
            if ((int?)ev.D("eventId") == 5101) Push(With(ev, "psych"));
        foreach (var ev in physio?.ReproEvents ?? new())
            if ((int?)ev.D("eventId") is 5004 or 5005 or 5006) Push(With(ev, "repro"));
        foreach (var ev in physio?.ObjectUsed ?? new()) Push(With(ev, "object"));

        return events.OrderBy(x => x.S("worldTime"), StringComparer.Ordinal).Take(3000).ToList();
    }

    public TickData BuildTickStore(string id)
    {
        var physio = Cache.Get<PhysioData>("physio-full:" + id);
        var psych = Cache.Get<PsychData>("psych-full:" + id);
        var behav = Cache.Get<BehaviorData>("behav-full:" + id);
        var goals = Cache.Get<GoalData>("goals:" + id);
        var rel = Cache.Get<RelData>("rel:" + id);
        var mem = Cache.Get<MemoryData>("memory:" + id);

        var events = new List<TickEvent>();
        void AddEv(string subsystem, JsonObject ev, string? label = null)
        {
            var wt = ev.S("worldTime");
            if (string.IsNullOrEmpty(wt)) return;
            events.Add(new TickEvent(wt, subsystem, (int?)ev.D("eventId"), ev.S("icon"), ev.S("level"),
                label ?? ev.S("label") ?? ev.S("action") ?? ev.S("kind") ?? "", ev));
        }
        foreach (var ev in physio?.SleepEvents ?? new()) AddEv("sleep", ev);
        foreach (var ev in physio?.ReproEvents ?? new()) AddEv("repro", ev);
        foreach (var ev in physio?.ObjectUsed ?? new()) AddEv("object", ev);
        foreach (var ev in psych?.PsychEvents ?? new()) AddEv("psych", ev);
        foreach (var ev in behav?.Actions ?? new()) AddEv("behavior", ev, ev.S("label") ?? ev.S("action"));
        foreach (var ev in behav?.Decisions ?? new()) AddEv("behavior", ev);
        foreach (var ev in behav?.ValuesEvents ?? new()) AddEv("values", ev);
        foreach (var ev in goals?.Events ?? new()) AddEv("goal", ev);
        foreach (var ev in mem?.Encoded ?? new())
            AddEv("memory", ev, !string.IsNullOrEmpty(ev.S("tag")) ? $"Memory: {ev.S("tag")}" : "Memory");
        foreach (var list in rel?.RelEvents.Values ?? Enumerable.Empty<List<JsonObject>>())
            foreach (var ev in list) AddEv("relationship", ev);

        List<JsonObject> ByWt(List<JsonObject>? l) => (l ?? new()).OrderBy(x => x.S("worldTime") ?? "", StringComparer.Ordinal).ToList();
        var physioSnaps = ByWt(physio?.Snapshots);
        var psychSnaps = ByWt(psych?.Snapshots);
        var behavSnaps = ByWt(behav?.Snapshots);

        var byTick = new Dictionary<string, List<TickEvent>>();
        foreach (var e in events)
        {
            if (!byTick.TryGetValue(e.WorldTime, out var l)) { l = new List<TickEvent>(); byTick[e.WorldTime] = l; }
            l.Add(e);
        }
        var tickKeys = byTick.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        Func<string, JsonObject?> Walker(List<JsonObject> snaps)
        {
            var i = 0; JsonObject? last = null;
            return wt =>
            {
                while (i < snaps.Count && Js.Cmp(snaps[i].S("worldTime"), wt) <= 0) last = snaps[i++];
                return last;
            };
        }
        var wp = Walker(physioSnaps); var wy = Walker(psychSnaps); var wb = Walker(behavSnaps);

        const int maxTicks = 6000;
        var truncated = tickKeys.Count > maxTicks;
        var keys = truncated ? tickKeys.Take(maxTicks).ToList() : tickKeys;
        var ticks = keys.Select(wt => new Tick(wt, new TickSnapshots(wp(wt), wy(wt), wb(wt)), byTick[wt])).ToList();
        return new TickData { Ticks = ticks, Count = ticks.Count, TotalTicks = tickKeys.Count, Truncated = truncated };
    }
}
