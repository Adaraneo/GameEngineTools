// Parsing/SnapshotParsers.cs — per-tick snapshot and psychology/reproduction event parsers
// (port of parsers/snapshots.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

public static class SnapshotParsers
{
    private const string N = P.N, NU = P.NU, Arr = P.Arr;

    private static readonly Regex PhysioRe = Js.Re($@"Energy:({NU})\s+Hunger:({NU})\s+Thirst:({NU})\s+Pain:({NU})\s+SleepDebt:({NU})h\s+Temp:({N})[°]?C\s+Immune:({NU})", false);
    private static readonly Regex PsychRe = Js.Re($@"Emotion:(\S+)\s+V:({N})\s+A:({N})\s+D:({N})\s+Stress:({NU})\s+CogLoad:({NU})", false);
    private static readonly Regex BehavRe = Js.Re($@"Plan:(\S+)\s+Rest:({NU})\s+Food:({NU})\s+Water:({NU})\s+Belonging:({NU})\s+Competence:({NU})\s+Intimacy:({NU})", false);
    private static readonly Regex SelfRe = Js.Re($@"esteem=({NU})\s+discrepancy=({NU})\s+\|\s+perc O:({NU})\s+C:({NU})\s+E:({NU})\s+A:({NU})\s+N:({NU})\s+\|\s+ideal E:({NU})\s+A:({NU})\s+C:({NU})", false);
    private static readonly Regex DarkCoreRe = Js.Re(@"DarkCore:([\d.,]+)\s+JustifyingBeliefs:([\d.,]+)");
    private static readonly Regex InterestRe = Js.Re(@"R:([\d.,]+)\s+I:([\d.,]+)\s+A:([\d.,]+)\s+S:([\d.,]+)\s+E:([\d.,]+)\s+C:([\d.,]+)\s+\|\s+base\s+R:([\d.,]+)\s+I:([\d.,]+)\s+A:([\d.,]+)\s+S:([\d.,]+)\s+E:([\d.,]+)\s+C:([\d.,]+)");
    private static readonly Regex NeedAppraisalRe = Js.Re($@"Competence\s+S:({NU})\s+F:({NU})\s+Relatedness\s+S:({NU})\s+F:({NU})\s+Autonomy\s+S:({NU})\s+F:({NU})\s+GlobalBalance:({N})", false);
    private static readonly Regex HormoneRe = Js.Re(@"Estradiol:([\d.,]+)\s+Progesterone:([\d.,]+)");
    private static readonly Regex LifeRe = Js.Re(@"(\w+)\s*→\s*(\w+)", false);
    private static readonly Regex NightmareRe = Js.Re(@"stres\s*\+([\d.,]+)[,\s]+valence\s*[–\-]([\d.,]+)");
    private static readonly Regex SleepQualRe = Js.Re(@"kvalita=?([\d.,]+).*?stres\s*\+([\d.,]+)");
    private static readonly Regex EmotionTransRe = Js.Re($@"(\w+){Arr}(\w+)\s+V:([\d.+\-,]+)\s+A:([\d.,]+)\s+stress=([\d.,]+)");
    private static readonly Regex MoodShiftRe = Js.Re($@"MoodBaseline\s*([\d.,]+){Arr}([\d.,]+)\s*\(Δ=([\d.+\-,]+)\)");
    private static readonly Regex AllostaticRe = Js.Re($@"AllostaticLoad\s+crossed\s+([\d.,]+):\s*([\d.,]+){Arr}([\d.,]+)\s*\(cortisol=([\d.,]+)\)");
    private static readonly Regex DeathRe = Js.Re(@"died of (\w+) at age (\d+) \(risk\/h=([\d.,]+)\)");
    private static readonly Regex ValuesSnapRe = Js.Re(@"Ben:([\d.,]+)\s+Uni:([\d.,]+)\s+SelfDir:([\d.,]+)\s+Stim:([\d.,]+)\s+Hed:([\d.,]+)\s+Ach:([\d.,]+)\s+Pow:([\d.,]+)\s+Sec:([\d.,]+)\s+Conf:([\d.,]+)\s+Trad:([\d.,]+)\s+\|\s+base\s+Ben:([\d.,]+)\s+Uni:([\d.,]+)\s+SelfDir:([\d.,]+)\s+Stim:([\d.,]+)\s+Hed:([\d.,]+)\s+Ach:([\d.,]+)\s+Pow:([\d.,]+)\s+Sec:([\d.,]+)\s+Conf:([\d.,]+)\s+Trad:([\d.,]+)");
    private static readonly Regex OtherParentRe = Js.Re(@"otherParent=([0-9a-f-]+)");
    private static readonly Regex ResultRe = Js.Re(@"result=(\w+)");
    private static readonly Regex DaysRe = Js.Re(@"daysPregnant=(\d+)");
    private static readonly Regex ChanceRe = Js.Re(@"chance=([\d.,]+)");

    /// <summary>worldTime + numeric fields read from payload keys.</summary>
    private static JsonObject FromPayload(LogEntry e, JsonObject p, params (string Out, string In)[] map)
    {
        var o = new JsonObject { ["worldTime"] = e.WorldTimeText };
        foreach (var (outKey, inKey) in map) o[outKey] = Js.Num(p.Get(inKey));
        return o;
    }

    /// <summary>worldTime + numeric fields read from consecutive regex groups starting at group 1.</summary>
    private static JsonObject FromMatch(LogEntry e, Mt m, params string[] keys)
    {
        var o = new JsonObject { ["worldTime"] = e.WorldTimeText };
        for (var i = 0; i < keys.Length; i++) o[keys[i]] = m.D(i + 1);
        return o;
    }

    private static (string, string) Same(string k) => (k, k);

    public static JsonObject? ParsePhysiology(LogEntry e)
    {
        if (e.Payload is { } p)
            return FromPayload(e, p,
                ("energy", "Energy"), ("hunger", "Hunger"), ("thirst", "Thirst"), ("pain", "Pain"), ("sleepDebt", "SleepDebt"),
                ("temp", "TempDelta"), ("immune", "Immune"), ("allostaticLoad", "Allostatic"), ("cortisol", "Cortisol"),
                ("testosterone", "Testosterone"), ("calories", "Calories"), ("vitaminD", "VitaminD"), ("iron", "Iron"),
                ("protein", "Protein"), ("bloodGlucose", "Glucose"), ("greyFraction", "Grey"), ("wrinkleScore", "Wrinkle"),
                ("hairDensity", "HairDensity"), ("muscleMass", "Muscle"), ("boneDensity", "Bone"));
        var m = Js.Match(e.Message, PhysioRe);
        return m == null ? null : FromMatch(e, m, "energy", "hunger", "thirst", "pain", "sleepDebt", "temp", "immune");
    }

    public static JsonObject? ParsePsychology(LogEntry e)
    {
        if (e.Payload is { } p)
        {
            var o = FromPayload(e, p, ("valence", "Valence"), ("arousal", "Arousal"), ("dominance", "Dominance"), ("stress", "Stress"),
                ("cogLoad", "CogLoad"), ("moodBaseline", "MoodBaseline"), ("needSocial", "NeedSocial"), ("needIntimacy", "NeedIntimacy"),
                ("needAchievement", "NeedAchievement"), ("needCare", "NeedCare"), ("needSafety", "NeedSafety"));
            o["emotion"] = p.Raw("Emotion");
            o["sicknessWithdraw"] = p.Get("SicknessWithdraw") != null ? Js.IsTrue(p.Get("SicknessWithdraw")) : null;
            return o;
        }
        var m = Js.Match(e.Message, PsychRe);
        if (m == null) return null;
        var r = FromMatch(e, m, "emotion", "valence", "arousal", "dominance", "stress", "cogLoad");
        r["emotion"] = m[1];
        return r;
    }

    public static JsonObject? ParseBehavior(LogEntry e)
    {
        if (e.Payload is { } p)
        {
            var o = FromPayload(e, p, ("rest", "Rest"), ("food", "Food"), ("water", "Water"), ("belonging", "Belonging"),
                ("competence", "Competence"), ("intimacy", "Intimacy"), ("intentStrength", "IntentStrength"));
            o["plan"] = p.Raw("Plan"); o["intentKind"] = p.Raw("IntentKind");
            o["intentCommitment"] = p.Get("IntentCommitment") != null ? Js.ToInt(p.Get("IntentCommitment")) : null;
            return o;
        }
        var m = Js.Match(e.Message, BehavRe);
        if (m == null) return null;
        var r = FromMatch(e, m, "plan", "rest", "food", "water", "belonging", "competence", "intimacy");
        r["plan"] = m[1];
        return r;
    }

    public static JsonObject? ParseSelfConcept(LogEntry e)
    {
        if (e.Payload is { } p)
            return FromPayload(e, p, ("esteem", "Esteem"), ("discrepancy", "Discrepancy"), ("percOpen", "PercOpen"), ("percCons", "PercCons"),
                ("percExtra", "PercExtra"), ("percAgree", "PercAgree"), ("percNeuro", "PercNeuro"), ("idealExtra", "IdealExtra"),
                ("idealAgree", "IdealAgree"), ("idealCons", "IdealCons"));
        var m = Js.Match(e.Message, SelfRe);
        return m == null ? null : FromMatch(e, m, "esteem", "discrepancy", "percOpen", "percCons", "percExtra", "percAgree", "percNeuro",
            "idealExtra", "idealAgree", "idealCons");
    }

    public static JsonObject? ParseDarkCoreSnapshot(LogEntry e)
    {
        if (e.Payload is { } p) return FromPayload(e, p, ("darkCore", "DarkCore"), ("justifyingBeliefs", "JustifyingBeliefs"));
        var m = Js.Match(e.Message, DarkCoreRe);
        return m == null ? null : FromMatch(e, m, "darkCore", "justifyingBeliefs");
    }

    private static readonly string[] InterestKeys =
    {
        "realistic", "investigative", "artistic", "social", "enterprising", "conventional",
        "baseRealistic", "baseInvestigative", "baseArtistic", "baseSocial", "baseEnterprising", "baseConventional",
    };

    public static JsonObject? ParseInterests(LogEntry e)
    {
        if (e.Payload is { } p)
            return FromPayload(e, p, InterestKeys.Select(k => (k, char.ToUpperInvariant(k[0]) + k[1..])).ToArray());
        var m = Js.Match(e.Message, InterestRe);
        return m == null ? null : FromMatch(e, m, InterestKeys);
    }

    private static readonly string[] NeedAppraisalKeys =
    {
        "competenceSatisfaction", "competenceFrustration", "relatednessSatisfaction", "relatednessFrustration",
        "autonomySatisfaction", "autonomyFrustration", "globalBalance",
    };

    public static JsonObject? ParseNeedAppraisal(LogEntry e)
    {
        if (e.Payload is { } p) return FromPayload(e, p, NeedAppraisalKeys.Select(Same).ToArray());
        var m = Js.Match(e.Message, NeedAppraisalRe);
        return m == null ? null : FromMatch(e, m, NeedAppraisalKeys);
    }

    public static JsonObject? ParseHormoneSnapshot(LogEntry e)
    {
        if (e.Payload is { } p) return FromPayload(e, p, ("estradiol", "Estradiol"), ("progesterone", "Progesterone"));
        var m = Js.Match(e.Message, HormoneRe);
        return m == null ? null : FromMatch(e, m, "estradiol", "progesterone");
    }

    private static readonly Dictionary<int, (string Icon, string Label, string Level)> LifeMeta = new()
    {
        [1600] = ("🎂", "Life stage", "info"), [1601] = ("🪞", "Life evaluation", "warning"),
    };

    public static JsonObject? ParseLifeStageEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || !LifeMeta.TryGetValue(eid, out var meta)) return null;
        JsonNode? from = null, to = null;
        if (e.Payload is { } p) { from = p.Raw("From"); to = p.Raw("To"); }
        else if (Js.Match(e.Message, LifeRe) is { } m) { from = m[1]; to = m[2]; }
        return new JsonObject
        {
            ["eventId"] = eid, ["worldTime"] = e.WorldTimeText, ["from"] = from, ["to"] = to, ["icon"] = meta.Icon, ["level"] = meta.Level,
            ["label"] = $"{meta.Label}: {Js.Str(from) ?? "?"} → {Js.Str(to) ?? "?"}",
        };
    }

    private static readonly Dictionary<int, (string Icon, string Label, string Level)> ReproMeta = new()
    {
        [5003] = ("🧬", "Conception evaluated", "secondary"), [5004] = ("🤰", "Pregnancy started", "success"),
        [5005] = ("💡", "Pregnancy discovered", "info"), [5006] = ("👶", "Child born", "primary"),
    };

    public static JsonObject? ParseReproEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || !ReproMeta.TryGetValue(eid, out var meta)) return null;
        var msg = e.Message ?? "";
        var o = new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = eid, ["icon"] = meta.Icon, ["label"] = meta.Label, ["level"] = meta.Level, ["message"] = msg,
        };
        if (e.Payload is { } p)
        {
            o["otherParent"] = p.Get("OtherParent") != null ? p.S("OtherParent")!.ToLowerInvariant() : null;
            o["result"] = p.Raw("Result");
            o["daysPregnant"] = p.Get("DaysPregnant") != null ? Js.ToInt(p.Get("DaysPregnant")) : null;
            o["chance"] = p.Get("Chance") != null ? Js.Num(p.Get("Chance")) : null;
            return o;
        }
        o["otherParent"] = Js.Match(msg, OtherParentRe)?[1]?.ToLowerInvariant();
        o["result"] = Js.Match(msg, ResultRe)?[1];
        o["daysPregnant"] = Js.Match(msg, DaysRe)?.I(1);
        o["chance"] = Js.Match(msg, ChanceRe)?.D(1);
        return o;
    }

    private static readonly Dictionary<int, (string Icon, string Label, string Level)> PsychEventMeta = new()
    {
        [5101] = ("👻", "Nightmare Effect", "danger"), [5102] = ("😰", "Sleep Quality → Stress", "warning"),
    };

    public static JsonObject? ParsePsychEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || !PsychEventMeta.TryGetValue(eid, out var meta)) return null;
        var msg = e.Message ?? "";
        var o = new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = eid, ["icon"] = meta.Icon, ["label"] = meta.Label, ["level"] = meta.Level, ["message"] = msg,
        };
        var p = e.Payload;
        if (eid == 5101)
        {
            if (p != null) { o["stressSpike"] = Js.Num(p.Get("StressSpike")); o["valencePenalty"] = Js.Num(p.Get("ValencePenalty")); return o; }
            if (Js.Match(msg, NightmareRe) is { } m) { o["stressSpike"] = m.D(1); o["valencePenalty"] = m.D(2); return o; }
        }
        if (eid == 5102)
        {
            if (p != null) { o["sleepQuality"] = Js.Num(p.Get("Quality")); o["stressDelta"] = Js.Num(p.Get("StressDelta")); return o; }
            if (Js.Match(msg, SleepQualRe) is { } m) { o["sleepQuality"] = m.D(1); o["stressDelta"] = m.D(2); return o; }
        }
        return o;
    }

    public static JsonObject? ParseEmotionTransition(LogEntry e)
    {
        if (e.EventId != 5103) return null;
        string? from, to; double? valence, arousal, stress;
        if (e.Payload is { } p)
        {
            from = p.S("OldEmotion"); to = p.S("NewEmotion");
            valence = Js.Num(p.Get("Valence")); arousal = Js.Num(p.Get("Arousal")); stress = Js.Num(p.Get("Stress"));
        }
        else
        {
            var m = Js.Match(e.Message, EmotionTransRe);
            if (m == null) return null;
            from = m[1]; to = m[2]; valence = m.D(3); arousal = m.D(4); stress = m.D(5);
        }
        return new JsonObject
        {
            ["eventId"] = 5103, ["worldTime"] = e.WorldTimeText, ["fromEmotion"] = from, ["toEmotion"] = to,
            ["valence"] = valence, ["arousal"] = arousal, ["stress"] = stress,
            ["icon"] = to != null && P.EmotionIcons.TryGetValue(to, out var ic) ? ic : "😐",
            ["label"] = $"{from} → {to}", ["level"] = "info",
        };
    }

    public static JsonObject? ParseMoodBaselineShifted(LogEntry e)
    {
        if (e.EventId != 5104) return null;
        double? oldB, newB, delta;
        if (e.Payload is { } p) { oldB = Js.Num(p.Get("OldBaseline")); newB = Js.Num(p.Get("NewBaseline")); delta = Js.Num(p.Get("Delta")); }
        else
        {
            var m = Js.Match(e.Message, MoodShiftRe);
            if (m == null) return null;
            oldB = m.D(1); newB = m.D(2); delta = m.D(3);
        }
        return new JsonObject
        {
            ["eventId"] = 5104, ["worldTime"] = e.WorldTimeText, ["oldBaseline"] = oldB, ["newBaseline"] = newB, ["delta"] = delta,
            ["icon"] = delta >= 0 ? "😊" : "😞", ["label"] = "Mood shift", ["level"] = delta >= 0 ? "success" : "warning",
        };
    }

    public static JsonObject? ParseAllostaticMilestone(LogEntry e)
    {
        if (e.EventId != 5105) return null;
        double? threshold, oldL, newL, cortisol;
        if (e.Payload is { } p)
        {
            threshold = Js.Num(p.Get("Threshold")); oldL = Js.Num(p.Get("OldLoad")); newL = Js.Num(p.Get("NewLoad")); cortisol = Js.Num(p.Get("Cortisol"));
        }
        else
        {
            var m = Js.Match(e.Message, AllostaticRe);
            threshold = m?.D(1); oldL = m?.D(2); newL = m?.D(3); cortisol = m?.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 5105, ["worldTime"] = e.WorldTimeText, ["threshold"] = threshold, ["oldLoad"] = oldL, ["newLoad"] = newL, ["cortisol"] = cortisol,
            ["icon"] = "📈", ["label"] = $"Allostatic >{(threshold == null ? "NaN" : Js.Fmt(threshold.Value))}",
            ["level"] = threshold >= 80 ? "danger" : "warning",
        };
    }

    public static JsonObject? ParseNaturalDeath(LogEntry e)
    {
        if (e.EventId != 5007) return null;
        string? cause; double? age, risk;
        if (e.Payload is { } p) { cause = p.S("Cause"); age = Js.ToInt(p.Get("Age")); risk = Js.Num(p.Get("Risk")); }
        else
        {
            var m = Js.Match(e.Message, DeathRe);
            if (m == null) return null;
            cause = m[1]; age = m.I(2); risk = m.D(3);
        }
        return new JsonObject
        {
            ["eventId"] = 5007, ["worldTime"] = e.WorldTimeText, ["cause"] = cause, ["age"] = age, ["riskPerHour"] = risk,
            ["icon"] = "\U0001F480", ["label"] = $"Died of {cause} aged {(age == null ? "NaN" : Js.Fmt(age.Value))}", ["level"] = "danger",
        };
    }

    private static readonly string[] ValueKeys =
    {
        "benevolence", "universalism", "selfDirection", "stimulation", "hedonism", "achievement", "power", "security", "conformity", "tradition",
        "baseBenevolence", "baseUniversalism", "baseSelfDirection", "baseStimulation", "baseHedonism", "baseAchievement", "basePower",
        "baseSecurity", "baseConformity", "baseTradition",
    };

    public static JsonObject? ParseValuesSnapshot(LogEntry e)
    {
        if (e.Payload is { } p)
            return FromPayload(e, p, ValueKeys.Select(k => (k, char.ToUpperInvariant(k[0]) + k[1..])).ToArray());
        var m = Js.Match(e.Message, ValuesSnapRe);
        return m == null ? null : FromMatch(e, m, ValueKeys);
    }
}
