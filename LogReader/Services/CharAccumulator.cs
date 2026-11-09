// Services/CharAccumulator.cs — single-pass per-character accumulator (port of createCharAccumulator in server.js).
// Feed entries via ProcessEntry(), then StoreCaches() to publish the derived data; ExportState() round-trips
// through the LogIndex so only newly appended log bytes need processing next time.
using System.Text.Json.Nodes;
using LogReader.Parsing;
using static LogReader.Services.Agg;

namespace LogReader.Services;

public sealed class CharAccumulator
{
    public const long SnapCacheTtlMs = 6L * 60 * 60 * 1000;

    private readonly string _id;
    private readonly Dictionary<string, List<JsonObject>> _lists = new();

    private readonly Dictionary<string, long> _bySubsystem, _byLevel, _byEventId;
    private string? _minRt, _maxRt;
    private long _totalEvents;
    private readonly Dictionary<string, Dictionary<string, long>> _freqH, _freqD;

    public CharAccumulator(string id, JsonObject? initial = null)
    {
        _id = id;
        _bySubsystem = ReadCounts(initial, "bySubsystem");
        _byLevel = ReadCounts(initial, "byLevel");
        _byEventId = ReadCounts(initial, "byEventId");
        _minRt = initial.S("minRt");
        _maxRt = initial.S("maxRt");
        _totalEvents = initial.L("totalEvents") ?? 0;
        _freqH = ReadFreq(initial, "freqH");
        _freqD = ReadFreq(initial, "freqD");
        _initial = initial;
    }

    private readonly JsonObject? _initial;

    private List<JsonObject> L(string name)
    {
        if (_lists.TryGetValue(name, out var l)) return l;
        l = Js.ToList(_initial.Get(name));
        _lists[name] = l;
        return l;
    }

    private static Dictionary<string, long> ReadCounts(JsonObject? o, string key)
    {
        var d = new Dictionary<string, long>();
        if (o.Obj(key) is { } src) foreach (var kv in src) d[kv.Key] = (long)(Js.NumberOf(kv.Value) ?? 0);
        return d;
    }

    private static Dictionary<string, Dictionary<string, long>> ReadFreq(JsonObject? o, string key)
    {
        var d = new Dictionary<string, Dictionary<string, long>>();
        if (o.Obj(key) is { } src)
            foreach (var kv in src)
                if (kv.Value is JsonObject inner)
                    d[kv.Key] = inner.ToDictionary(x => x.Key, x => (long)(Js.NumberOf(x.Value) ?? 0));
        return d;
    }

    private static readonly string[] ListNames =
    {
        "physio.snapshots", "physio.sleepEvents", "physio.cycle", "physio.actions", "physio.reproEvents", "physio.hormones", "psych.snapshots", "psych.psychEvents", "psych.selfConcept", "psych.interests", "psych.darkCoreSnapshots", "psych.needAppraisal", "psych.valuesSnapshots", "behav.snapshots", "behav.actions", "behav.habits", "behav.decisions", "behav.plans", "behav.darkCoreEvents", "behav.travelEvents", "behavCandidates", "behavRunning", "behavCooldowns", "schedSysEvts", "mem.encoded", "mem.consolidated", "mem.reconsolidated", "mem.beliefUpdates", "goalEvts", "locContextEvts", "locMoveEvts", "locObjEvts", "schedEvts", "relEdges", "relApplies", "relLogEvts", "int1201Evts", "int1202Evts", "int1203Evts", "intContexts", "intentEvts", "valuesEvts", "dialogueEvts", "objUsedEvts", "objRespawnEvts",
    };

    // Named lists (names double as the persisted keys).
    private List<JsonObject> PhysioSnapshots => L("physio.snapshots");
    private List<JsonObject> PhysioSleep => L("physio.sleepEvents");
    private List<JsonObject> PhysioCycle => L("physio.cycle");
    private List<JsonObject> PhysioActions => L("physio.actions");
    private List<JsonObject> PhysioRepro => L("physio.reproEvents");
    private List<JsonObject> PhysioHormones => L("physio.hormones");
    private List<JsonObject> PsychSnapshots => L("psych.snapshots");
    private List<JsonObject> PsychEvents => L("psych.psychEvents");
    private List<JsonObject> PsychSelf => L("psych.selfConcept");
    private List<JsonObject> PsychInterests => L("psych.interests");
    private List<JsonObject> PsychDarkCore => L("psych.darkCoreSnapshots");
    private List<JsonObject> PsychNeed => L("psych.needAppraisal");
    private List<JsonObject> PsychValues => L("psych.valuesSnapshots");
    private List<JsonObject> BehavSnapshots => L("behav.snapshots");
    private List<JsonObject> BehavActions => L("behav.actions");
    private List<JsonObject> BehavHabits => L("behav.habits");
    private List<JsonObject> BehavDecisions => L("behav.decisions");
    private List<JsonObject> BehavPlans => L("behav.plans");
    private List<JsonObject> BehavDarkCore => L("behav.darkCoreEvents");
    private List<JsonObject> BehavTravel => L("behav.travelEvents");
    private List<JsonObject> BehavCandidates => L("behavCandidates");
    private List<JsonObject> BehavRunning => L("behavRunning");
    private List<JsonObject> BehavCooldowns => L("behavCooldowns");
    private List<JsonObject> SchedSys => L("schedSysEvts");
    private List<JsonObject> MemEncoded => L("mem.encoded");
    private List<JsonObject> MemConsolidated => L("mem.consolidated");
    private List<JsonObject> MemReconsolidated => L("mem.reconsolidated");
    private List<JsonObject> MemBeliefs => L("mem.beliefUpdates");
    private List<JsonObject> GoalEvts => L("goalEvts");
    private List<JsonObject> LocContext => L("locContextEvts");
    private List<JsonObject> LocMove => L("locMoveEvts");
    private List<JsonObject> LocObj => L("locObjEvts");
    private List<JsonObject> SchedEvts => L("schedEvts");
    private List<JsonObject> RelEdges => L("relEdges");
    private List<JsonObject> RelApplies => L("relApplies");
    private List<JsonObject> RelLog => L("relLogEvts");
    private List<JsonObject> Int1201 => L("int1201Evts");
    private List<JsonObject> Int1202 => L("int1202Evts");
    private List<JsonObject> Int1203 => L("int1203Evts");
    private List<JsonObject> IntContexts => L("intContexts");
    private List<JsonObject> IntentEvts => L("intentEvts");
    private List<JsonObject> ValuesEvts => L("valuesEvts");
    private List<JsonObject> DialogueEvts => L("dialogueEvts");
    private List<JsonObject> ObjUsed => L("objUsedEvts");
    private List<JsonObject> ObjRespawn => L("objRespawnEvts");

    public void ProcessEntry(LogEntry e)
    {
        var eid = e.EventId ?? -1;
        var ss = e.Subsystem; var lv = e.Level; var rt = e.RealTimestamp; var wt = e.WorldTimeText;

        _totalEvents++;
        if (!string.IsNullOrEmpty(ss)) _bySubsystem[ss] = _bySubsystem.GetValueOrDefault(ss) + 1;
        if (!string.IsNullOrEmpty(lv)) _byLevel[lv] = _byLevel.GetValueOrDefault(lv) + 1;
        if (e.EventId != null) { var k = eid.ToString(); _byEventId[k] = _byEventId.GetValueOrDefault(k) + 1; }
        if (!string.IsNullOrEmpty(rt))
        {
            if (_minRt == null || Js.Cmp(rt, _minRt) < 0) _minRt = rt;
            if (_maxRt == null || Js.Cmp(rt, _maxRt) > 0) _maxRt = rt;
        }

        if (!string.IsNullOrEmpty(wt))
        {
            var sub = ss ?? "Unknown";
            var hk = wt.Length > 13 ? wt[..13] : wt;
            var dk = wt.Length > 10 ? wt[..10] : wt;
            Bump(_freqH, hk, sub); Bump(_freqD, dk, sub);
        }

        if (EventIds.Physio.Contains(eid))
        {
            switch (eid)
            {
                case 5000: Push(ParsePhysiology(e), PhysioSnapshots); break;
                case 5001: PushR(SleepParsers.ParsePhysioCycle(e), PhysioCycle); break;
                case 5002:
                    if (SleepParsers.ParsePhysioSleepEnded(e) is { } se)
                    {
                        se["eventId"] = 5002; se["label"] = "Sleep Ended"; se["icon"] = "💤"; se["level"] = "info";
                        CapPushRecent(PhysioSleep, se);
                    }
                    break;
                case 1003: PushR(BehaviorParsers.ParseAction1003(e), PhysioActions); break;
                case 5003 or 5004 or 5005 or 5006: PushR(SnapshotParsers.ParseReproEvent(e), PhysioRepro); break;
                case 5007: PushR(SnapshotParsers.ParseNaturalDeath(e), PhysioSleep); break;
                case 5600: Push(SnapshotParsers.ParseHormoneSnapshot(e), PhysioHormones); break;
                default:
                    if (eid >= 1100 && eid <= 1113) PushR(SleepParsers.ParseSleepEvent(e), PhysioSleep);
                    break;
            }
        }

        if (EventIds.Psych.Contains(eid))
        {
            if (eid == 5100) Push(SnapshotParsers.ParsePsychology(e), PsychSnapshots);
            else PushR(SnapshotParsers.ParsePsychEvent(e), PsychEvents);
        }
        if (EventIds.PsychNew.Contains(eid))
        {
            if (eid == 5103) PushR(SnapshotParsers.ParseEmotionTransition(e), PsychEvents);
            if (eid == 5104) PushR(SnapshotParsers.ParseMoodBaselineShifted(e), PsychEvents);
            if (eid == 5105) PushR(SnapshotParsers.ParseAllostaticMilestone(e), PhysioSleep);
        }
        if (EventIds.Self.Contains(eid)) Push(SnapshotParsers.ParseSelfConcept(e), PsychSelf);
        if (EventIds.Life.Contains(eid)) PushR(SnapshotParsers.ParseLifeStageEvent(e), PsychEvents);
        if (EventIds.Interest.Contains(eid)) Push(SnapshotParsers.ParseInterests(e), PsychInterests);
        if (EventIds.DarkCoreSnap.Contains(eid)) Push(SnapshotParsers.ParseDarkCoreSnapshot(e), PsychDarkCore);
        if (EventIds.ValuesSnap.Contains(eid)) Push(SnapshotParsers.ParseValuesSnapshot(e), PsychValues);
        if (EventIds.NeedAppraisal.Contains(eid)) Push(SnapshotParsers.ParseNeedAppraisal(e), PsychNeed);
        if (EventIds.DarkCoreMod.Contains(eid)) PushR(BehaviorParsers.ParseDarkCoreModifier(e), BehavDarkCore);
        if (EventIds.MutKnow.Contains(eid)) PushR(MemoryParsers.ParseMutualKnowledge(e), PsychEvents);

        if (EventIds.Behav.Contains(eid))
        {
            switch (eid)
            {
                case 5200: Push(SnapshotParsers.ParseBehavior(e), BehavSnapshots); break;
                case 1000: PushR(BehaviorParsers.ParseCandidate(e), BehavCandidates); break;
                case 1001: PushR(BehaviorParsers.ParseDecision(e), BehavDecisions); break;
                case 1002: PushR(BehaviorParsers.ParseRunning(e), BehavRunning); break;
                case 1003: PushR(BehaviorParsers.ParseAction1003(e), BehavActions); break;
                case 1004: PushR(BehaviorParsers.ParseCooldownSet(e), BehavCooldowns); break;
                case 1010: PushR(GoalParsers.ParseIntentBias(e), IntentEvts); break;
                case 1011: PushR(BehaviorParsers.ParseHabit(e), BehavHabits); break;
                case 1012: PushR(BehaviorParsers.ParseHabitDecayed(e), BehavHabits); break;
                case 1013: PushR(BehaviorParsers.ParseHabitPruned(e), BehavHabits); break;
                case 1014: PushR(BehaviorParsers.ParseHabitBias(e), BehavHabits); break;
                case 1015: PushR(BehaviorParsers.ParseHabitDominated(e), BehavHabits); break;
                case 1016: PushR(BehaviorParsers.ParseNeedThresholdCrossed(e), BehavDecisions); break;
                case 5201: PushR(BehaviorParsers.ParsePlanDetail(e), BehavPlans); break;
            }
        }
        if (EventIds.Sched.Contains(eid)) PushR(ScheduleParsers.ParseSchedulerEvent(e), SchedSys);
        if (EventIds.Travel.Contains(eid)) PushR(MovementParsers.ParseTravelEvent(e), BehavTravel);

        if (eid == 3000) PushR(MemoryParsers.ParseEncoded(e), MemEncoded);
        if (eid == 3001) PushR(MemoryParsers.ParseConsolidated(e), MemConsolidated);
        if (eid == 3002) PushR(MemoryParsers.ParseReconsolidated(e), MemReconsolidated);
        if (eid == 3003) PushR(MemoryParsers.ParseBeliefUpdated(e), MemBeliefs);

        if (EventIds.Goal.Contains(eid)) PushR(GoalParsers.ParseGoalEvent(e), GoalEvts);

        if (EventIds.Location.Contains(eid))
        {
            if (eid == 1200 && InteractionParsers.ParseContext(e) is { } c) LocContext.Add(c);
            if (eid == 1205) PushR(InteractionParsers.ParseObjectInteraction(e), LocObj);
            if (eid == 1206) PushR(InteractionParsers.ParseMoveAction(e), LocMove);
        }

        if (EventIds.Schedule.Contains(eid)) PushR(ScheduleParsers.ParseScheduleEvent(e), SchedEvts);

        if (EventIds.Interact.Contains(eid))
        {
            switch (eid)
            {
                case 1200: if (InteractionParsers.ParseContext(e) is { } ic) IntContexts.Add(ic); break;
                case 1201: PushR(InteractionParsers.ParseDecision(e), Int1201); break;
                case 1202: PushR(InteractionParsers.ParseTouchDecision(e), Int1202); break;
                case 1203: PushR(InteractionParsers.ParseSexualProposal(e), Int1203); break;
            }
        }

        if (eid >= 1005 && eid <= 1009) PushR(GoalParsers.ParseIntentEvent(e), IntentEvts);

        if (EventIds.Values.Contains(eid))
        {
            if (eid == 1210 && ValuesParsers.ParseProfile(e) is { } vp) ValuesEvts.Add(vp);
            if (eid == 1211) PushR(ValuesParsers.ParseCongruenceViolation(e), ValuesEvts);
            if (eid == 1212) PushR(ValuesParsers.ParseGuiltSpike(e), ValuesEvts);
            if (eid == 1213) PushR(ValuesParsers.ParseDrifted(e), ValuesEvts);
        }

        if (EventIds.Dialogue.Contains(eid)) PushR(DialogueParsers.ParseSpeechActUttered(e), DialogueEvts);

        if (EventIds.Obj.Contains(eid))
        {
            if (eid == 1500) PushR(ObjectParsers.ParseObjectUsed(e), ObjUsed);
            if (eid == 1501) PushR(ObjectParsers.ParseObjectRespawned(e), ObjRespawn);
        }

        if (eid == 2005)
        {
            var p = RelationshipParsers.ParseEdge(e);
            if (p != null && p.S("from") == _id) CapPush(RelEdges, p);
        }
        else if (eid == 2002 && e.PersonLower == _id)
        {
            Push(RelationshipParsers.ParseApply(e), RelApplies);
        }

        if (EventIds.SocialCmp.Contains(eid)) PushR(RelationshipParsers.ParseSocialComparison(e), PsychEvents);

        var rec = RelLogRecordFor(e, _id);
        if (rec != null) CapPushRecent(RelLog, rec);
    }

    private static JsonObject? ParsePhysiology(LogEntry e) => SnapshotParsers.ParsePhysiology(e);
    private static void Push(JsonObject? p, List<JsonObject> list) { if (p != null) CapPush(list, p); }
    private static void PushR(JsonObject? p, List<JsonObject> list) { if (p != null) CapPushRecent(list, p); }

    private static void Bump(Dictionary<string, Dictionary<string, long>> m, string bucket, string sub)
    {
        if (!m.TryGetValue(bucket, out var d)) { d = new Dictionary<string, long>(); m[bucket] = d; }
        d[sub] = d.GetValueOrDefault(sub) + 1;
    }

    /// <summary>Publishes everything derived from the accumulated entries into the cache; returns the event total.</summary>
    public long StoreCaches(TtlCache cache)
    {
        var id = _id;
        cache.Set("summary:" + id, new SummaryData
        {
            TotalEvents = _totalEvents, BySubsystem = new(_bySubsystem), ByEventId = new(_byEventId), ByLevel = new(_byLevel), From = _minRt, To = _maxRt,
        });
        cache.Set("freq:" + id + ":hour", BuildFreq(_freqH));
        cache.Set("freq:" + id + ":day", BuildFreq(_freqD));

        var objUsed = ObjUsed; var objRespawn = ObjRespawn;
        cache.Set("physio-full:" + id, new PhysioData
        {
            Snapshots = PhysioSnapshots, SleepEvents = PhysioSleep, Cycle = PhysioCycle, Actions = PhysioActions,
            ReproEvents = PhysioRepro, Hormones = PhysioHormones, ObjectUsed = objUsed, ObjectRespawned = objRespawn,
        }, SnapCacheTtlMs);
        cache.Set("psych-full:" + id, new PsychData
        {
            Snapshots = PsychSnapshots, PsychEvents = PsychEvents, SelfConcept = PsychSelf, Interests = PsychInterests,
            DarkCoreSnapshots = PsychDarkCore, NeedAppraisal = PsychNeed, ValuesSnapshots = PsychValues,
        }, SnapCacheTtlMs);

        var intents = IntentEvts.OrderBy(x => x.S("worldTime") ?? "", StringComparer.Ordinal).ToList();
        cache.Set("behav-full:" + id, new BehaviorData
        {
            Snapshots = BehavSnapshots, Actions = BehavActions, Habits = BehavHabits, Decisions = BehavDecisions, Plans = BehavPlans,
            DarkCoreEvents = BehavDarkCore, TravelEvents = BehavTravel, Candidates = BehavCandidates, Running = BehavRunning,
            Cooldowns = BehavCooldowns, ValuesEvents = ValuesEvts, Intents = intents,
        }, SnapCacheTtlMs);
        cache.Set("memory:" + id, new MemoryData
        {
            Encoded = MemEncoded, Consolidated = MemConsolidated, Reconsolidated = MemReconsolidated, BeliefUpdates = MemBeliefs,
        }, SnapCacheTtlMs);
        cache.Set("sched-sys:" + id, new ScheduleData { Events = SchedSys }, SnapCacheTtlMs);

        cache.Set("goals:" + id, new GoalData
        {
            Events = GoalEvts, Timelines = GoalParsers.BuildTimelines(GoalEvts), ActiveGoals = BuildActiveGoals(GoalEvts),
        }, SnapCacheTtlMs);
        cache.Set("schedule:" + id, new ScheduleData { Events = SchedEvts }, SnapCacheTtlMs);

        var allActionsForLoc = LocMove.Concat(LocObj).Concat(PhysioActions).ToList();
        var (sessions, byLocation) = InteractionParsers.BuildLocationSessions(LocContext, LocMove, allActionsForLoc);
        cache.Set("locations:" + id, new LocationData { Sessions = sessions, ByLocation = byLocation, ContextEvents = LocContext, ObjectInteractions = LocObj }, SnapCacheTtlMs);

        cache.Set("rel-edges:" + id, RelEdges, SnapCacheTtlMs);
        cache.Set("rel:" + id, BuildRelData(RelEdges, RelApplies, RelLog), SnapCacheTtlMs);

        var intData = BuildInteractionData(Int1201, Int1202, Int1203, IntContexts, RelLog, id);
        intData.IntentEvents = intents;
        intData.Utterances = DialogueEvts.OrderBy(x => x.S("worldTime") ?? "", StringComparer.Ordinal).ToList();
        cache.Set("interactions:" + id, intData, SnapCacheTtlMs);

        return _totalEvents;
    }

    /// <summary>Serializable snapshot of the accumulator; the caller adds offsets before persisting.</summary>
    public Dictionary<string, object?> ExportState()
    {
        var d = new Dictionary<string, object?>
        {
            ["bySubsystem"] = _bySubsystem, ["byLevel"] = _byLevel, ["byEventId"] = _byEventId,
            ["minRt"] = _minRt, ["maxRt"] = _maxRt, ["totalEvents"] = _totalEvents, ["freqH"] = _freqH, ["freqD"] = _freqD,
        };
        foreach (var name in ListNames) d[name] = L(name);
        return d;
    }
}
