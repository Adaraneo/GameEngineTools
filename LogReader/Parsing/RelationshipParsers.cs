// Parsing/RelationshipParsers.cs — relationship engine parsers (port of parsers/relationships.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

public static class RelationshipParsers
{
    private static readonly Regex EdgeRe = Js.Re(
        @"Hrana\s+([0-9a-f-]+)\s*[→>]\s*([0-9a-f-]+)\s*:\s*" +
        $@"Like=({P.N}),\s*Trust=({P.N}),\s*Closeness=({P.N}),\s*Comfort=({P.N}),\s*Respect=({P.N}),\s*Familiarity=({P.N}),\s*(?:IntimateAffinity|RomanticInterest)=({P.N}),\s*SexualInterest=({P.N}),\s*AestheticAttraction=({P.N}),\s*PhysicalAttraction=({P.N})");
    private static readonly Regex OtherRe = Js.Re(@"other=([0-9a-f-]+)");
    private static readonly Regex DimChangeRe = new(@"(\w+):(\d+(?:[.,]\d+)?)[→>](\d+(?:[.,]\d+)?)", RegexOptions.Compiled);
    private static readonly Regex DimValueRe = new(@"(\w+)=([\d.,]+)", RegexOptions.Compiled);
    private static readonly Regex EventTypeRe = Js.Re(@"\[REL/EVENT\]\s+\S+\s+(\w+)");
    private static readonly Regex OutcomeWordRe = Js.Re(@"outcome=(\w+)");
    private static readonly Regex ActRe = Js.Re(@"act=(\w+)");
    private static readonly Regex RoleRe = Js.Re(@"role=(\w+)");
    private static readonly Regex LikeRe = Js.Re(@"Like=?([\d.,]+)");
    private static readonly Regex AttractionRe = Js.Re(@"Attraction=?([\d.,]+)");
    private static readonly Regex SexOutcomeRe = Js.Re(@"outcome=(\w+),\s*intimateAffinity=([\d.,]+),\s*sexualInterest=([\d.,]+),\s*closeness=([\d.,]+)");
    private static readonly Regex DecayDetailRe = Js.Re($@"Edge\s+([0-9a-f-]+)\s*{P.Arr}\s*([0-9a-f-]+):\s*(.+)");
    private static readonly Regex MilestoneRe = Js.Re($@"([0-9a-f-]+)\s*{P.Arr}\s*([0-9a-f-]+):\s*(\w+)\s+crossed\s+([\d.,]+)\s*\(([\d.,]+){P.Arr}([\d.,]+)\)");
    private static readonly Regex ThirdPartyRe = Js.Re($@"observed\s+\S+\s+act\s+on\s+\S+:\s*(\w+)\s+([\d.,]+){P.Arr}([\d.,]+)\s*\(weight=([\d.,]+)\)");
    private static readonly Regex JealousyRe = Js.Re($@"transgressionResidue\s*([\d.,]+){P.Arr}([\d.,]+)");
    private static readonly Regex OneWayRe = Js.Re($@"Jednostrann[ée]\s+pozorov[aá]n[ií]:\s*([0-9a-f-]+)\s*{P.Arr}\s*([0-9a-f-]+)\s+Like=([\d.,]+)\s+Attraction=([\d.,]+)");
    private static readonly Regex InvestRe = Js.Re(@"commitment=([\d.,]+)\s*\(c[ií]l\s*([\d.,]+)\)\s*investice=([\d.,]+)\s*alternativa=([\d.,]+)");
    private static readonly Regex SocialCompareRe = Js.Re(@"compared to ([0-9a-f-]+):\s*direction=(\w+)\s+reaction=(\w+)\s+envy=(\w+)\s+selfEsteemDelta=([0-9.+\-,]+)\s+moodValenceDelta=([0-9.+\-,]+)\s+moodBaselineDelta=([0-9.+\-,]+)\s+achieveDelta=([0-9.+\-,]+)\s+hostilityDelta=([0-9.,]+)");
    private static readonly Regex RepPriorRe = Js.Re(@"observer=([0-9a-f-]+)\s+subject=([0-9a-f-]+):\s*reputationPrior=([0-9.,]+)\s+trustShift=([0-9.+\-,]+)");

    private static string Lower(string? s) => (s ?? "").ToLowerInvariant();
    private static JsonNode? OptNum(JsonObject p, string key) => p.Get(key) != null ? Js.Num(p.Get(key)) : null;
    private static JsonNode? OptBool(JsonObject p, string key) => p.Get(key) != null ? Js.IsTrue(p.Get(key)) : null;

    public static JsonObject? ParseEdge(LogEntry e)
    {
        if (e.Payload is { } p)
        {
            return new JsonObject
            {
                ["worldTime"] = e.WorldTimeText, ["from"] = Lower(p.S("From")?.Trim()), ["to"] = Lower(p.S("To")?.Trim()),
                ["like"] = Js.Num(p.Get("Like")), ["trust"] = Js.Num(p.Get("Trust")), ["closeness"] = Js.Num(p.Get("Closeness")),
                ["comfort"] = Js.Num(p.Get("Comfort")), ["respect"] = Js.Num(p.Get("Respect")), ["familiarity"] = Js.Num(p.Get("Familiarity")),
                ["intimateAffinity"] = Js.Num(p.Get("IntimateAffinity")), ["sexualInterest"] = Js.Num(p.Get("Sexual")),
                ["aestheticAttraction"] = Js.Num(p.Get("Aesthetic")), ["physicalAttraction"] = Js.Num(p.Get("Physical")),
                ["communalStrength"] = OptNum(p, "Communal"), ["exchangeStrength"] = OptNum(p, "Exchange"),
                ["transgressionResidue"] = OptNum(p, "Residue"), ["responsiveDesire"] = OptNum(p, "Responsive"),
                ["perceivedDominance"] = OptNum(p, "Dominance"), ["perceivedPrestige"] = OptNum(p, "Prestige"),
                ["contemptDestroyed"] = OptBool(p, "Contempt"), ["commitment"] = OptNum(p, "Commitment"),
                ["investmentSize"] = OptNum(p, "InvestmentSize"), ["alternativeQuality"] = OptNum(p, "AlternativeQuality"),
                ["dissolutionConsidered"] = OptBool(p, "DissolutionConsidered"),
            };
        }
        var m = Js.Match(e.Message, EdgeRe);
        if (m == null) return null;
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["from"] = m[1]!.Trim().ToLowerInvariant(), ["to"] = m[2]!.Trim().ToLowerInvariant(),
            ["like"] = m.D(3), ["trust"] = m.D(4), ["closeness"] = m.D(5), ["comfort"] = m.D(6), ["respect"] = m.D(7),
            ["familiarity"] = m.D(8), ["intimateAffinity"] = m.D(9), ["sexualInterest"] = m.D(10),
            ["aestheticAttraction"] = m.D(11), ["physicalAttraction"] = m.D(12),
            ["commitment"] = null, ["investmentSize"] = null, ["alternativeQuality"] = null, ["dissolutionConsidered"] = null,
        };
    }

    public static JsonObject? ParseApply(LogEntry e)
    {
        var msg = e.Message ?? "";
        if (!msg.Contains("[REL/APPLY]", StringComparison.Ordinal)) return null;
        var other = Js.Match(msg, OtherRe);
        if (other == null) return null;
        var dims = new JsonObject();
        foreach (Match dm in DimChangeRe.Matches(msg))
            dims[dm.Groups[1].Value.ToLowerInvariant()] = Js.Num(dm.Groups[3].Value);
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["relatedPersonId"] = other[1]!.ToLowerInvariant(),
            ["interactionId"] = e.InteractionId, ["dims"] = dims,
        };
    }

    private static readonly HashSet<string> DimWhitelist = new()
    {
        "like", "trust", "closeness", "comfort", "respect", "familiarity", "intimateaffinity", "romanticinterest",
        "sexualinterest", "aestheticattraction", "physicalattraction", "attraction",
    };

    private static readonly HashSet<string> DimValueKeys = new() { "like", "trust", "closeness", "comfort", "respect", "familiarity", "attraction" };

    private static JsonObject? ParseDimChanges(string msg)
    {
        var changes = new JsonObject();
        foreach (Match m in DimChangeRe.Matches(msg))
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            if (DimWhitelist.Contains(key))
                changes[key] = new JsonObject { ["from"] = Js.Num(m.Groups[2].Value), ["to"] = Js.Num(m.Groups[3].Value) };
        }
        return changes.Count > 0 ? changes : null;
    }

    private static JsonObject? ParseDimValues(string msg)
    {
        var vals = new JsonObject();
        foreach (Match m in DimValueRe.Matches(msg))
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            if (DimValueKeys.Contains(key)) vals[key] = Js.Num(m.Groups[2].Value);
        }
        return vals.Count > 0 ? vals : null;
    }

    public static JsonObject ParseRelEventForLog(LogEntry e)
    {
        var eid = e.EventId;
        var msg = e.Message ?? "";
        JsonObject Base(string kind) => new()
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = eid, ["partnerId"] = Lower(e.RelatedPersonId),
            ["interactionId"] = e.InteractionId, ["kind"] = kind,
        };
        switch (eid)
        {
            case 2001:
            {
                var o = Base("event");
                o["eventType"] = Js.Match(msg, EventTypeRe)?[1]; o["outcome"] = Js.Match(msg, OutcomeWordRe)?[1];
                o["act"] = Js.Match(msg, ActRe)?[1]; o["role"] = Js.Match(msg, RoleRe)?[1];
                return o;
            }
            case 2002:
            {
                var o = Base("apply");
                o["outcome"] = Js.Match(msg, OutcomeWordRe)?[1]; o["changes"] = ParseDimChanges(msg);
                return o;
            }
            case 2004: return Base("new_edge");
            case 2005: { var o = Base("snapshot"); o["values"] = ParseDimValues(msg); return o; }
            case 2006: { var o = Base("decay"); o["changes"] = ParseDimChanges(msg); return o; }
            case 2007:
            {
                var o = Base("first_impression");
                o["like"] = Js.Match(msg, LikeRe)?.D(1); o["attraction"] = Js.Match(msg, AttractionRe)?.D(1);
                return o;
            }
            case 1201 or 1202 or 1203:
            {
                var oc = InteractionParsers.ParseOutcome(e);
                if (oc != null) return oc;
                var o = Base("interaction_outcome");
                return o;
            }
            default: return Base("unknown");
        }
    }

    public static JsonObject? ParseSexualEncounterOutcome(LogEntry e)
    {
        if (e.EventId != 2008) return null;
        string? outcome; double? intimate, sexual, closeness;
        if (e.Payload is { } p)
        {
            outcome = p.S("Outcome") ?? "unknown"; intimate = Js.Num(p.Get("IntimateAffinity"));
            sexual = Js.Num(p.Get("SexualInterest")); closeness = Js.Num(p.Get("Closeness"));
        }
        else
        {
            var m = Js.Match(e.Message, SexOutcomeRe);
            outcome = m?[1] ?? "unknown"; intimate = m?.D(2); sexual = m?.D(3); closeness = m?.D(4);
        }
        var accepted = outcome == "accepted";
        return new JsonObject
        {
            ["eventId"] = 2008, ["worldTime"] = e.WorldTimeText, ["partnerId"] = Lower(e.RelatedPersonId), ["outcome"] = outcome,
            ["intimateAffinity"] = intimate, ["sexualInterest"] = sexual, ["closeness"] = closeness,
            ["icon"] = accepted ? "💕" : "🚫", ["label"] = "Sexual encounter", ["level"] = accepted ? "danger" : "secondary",
            ["kind"] = "sexual_outcome",
        };
    }

    public static JsonObject? ParseDecayDetail(LogEntry e)
    {
        if (e.EventId != 2009) return null;
        var msg = e.Message ?? "";
        var m = Js.Match(msg, DecayDetailRe);
        if (m == null) return null;
        return new JsonObject
        {
            ["eventId"] = 2009, ["worldTime"] = e.WorldTimeText, ["from"] = m[1]!.ToLowerInvariant(), ["to"] = m[2]!.ToLowerInvariant(),
            ["changes"] = ParseDimChanges(msg), ["icon"] = "📉", ["label"] = "Decay detail", ["level"] = "secondary", ["kind"] = "decay",
        };
    }

    public static JsonObject? ParseMilestone(LogEntry e)
    {
        if (e.EventId != 2010) return null;
        string from, partner; string? dim; double? threshold, oldV, newV;
        if (e.Payload is { } p)
        {
            from = Lower(p.S("From")); partner = Lower(p.S("To")); dim = p.S("Dimension");
            threshold = Js.Num(p.Get("Threshold")); oldV = Js.Num(p.Get("OldValue")); newV = Js.Num(p.Get("NewValue"));
        }
        else
        {
            var m = Js.Match(e.Message, MilestoneRe);
            if (m == null) return null;
            from = m[1]!.ToLowerInvariant(); partner = m[2]!.ToLowerInvariant(); dim = m[3];
            threshold = m.D(4); oldV = m.D(5); newV = m.D(6);
        }
        return new JsonObject
        {
            ["eventId"] = 2010, ["worldTime"] = e.WorldTimeText, ["from"] = from, ["partnerId"] = partner, ["dimension"] = dim,
            ["threshold"] = threshold, ["oldValue"] = oldV, ["newValue"] = newV,
            ["icon"] = "🏆", ["label"] = $"{dim} milestone {(threshold == null ? "NaN" : Js.Fmt(threshold.Value))}",
            ["level"] = "success", ["kind"] = "milestone",
        };
    }

    public static JsonObject? ParseThirdPartyReputation(LogEntry e)
    {
        if (e.EventId != 2011) return null;
        string? dim; double? oldV, newV, weight;
        if (e.Payload is { } p)
        {
            dim = p.S("Dimension"); oldV = Js.Num(p.Get("OldValue")); newV = Js.Num(p.Get("NewValue")); weight = Js.Num(p.Get("Weight"));
        }
        else
        {
            var m = Js.Match(e.Message, ThirdPartyRe);
            dim = m?[1]; oldV = m?.D(2); newV = m?.D(3); weight = m?.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 2011, ["worldTime"] = e.WorldTimeText, ["partnerId"] = Lower(e.RelatedPersonId), ["dimension"] = dim,
            ["oldValue"] = oldV, ["newValue"] = newV, ["weight"] = weight,
            ["icon"] = "👀", ["label"] = "Third-party rep.", ["level"] = "info", ["kind"] = "third_party",
        };
    }

    public static JsonObject? ParseJealousy(LogEntry e)
    {
        if (e.EventId != 2012) return null;
        double? oldR, newR;
        if (e.Payload is { } p) { oldR = Js.Num(p.Get("OldResidue")); newR = Js.Num(p.Get("NewResidue")); }
        else
        {
            var m = Js.Match(e.Message, JealousyRe);
            oldR = m?.D(1); newR = m?.D(2);
        }
        return new JsonObject
        {
            ["eventId"] = 2012, ["worldTime"] = e.WorldTimeText, ["partnerId"] = Lower(e.RelatedPersonId),
            ["oldResidue"] = oldR, ["newResidue"] = newR, ["icon"] = "😤", ["label"] = "Jealousy distress", ["level"] = "danger", ["kind"] = "jealousy",
        };
    }

    public static JsonObject? ParseOneWayObservation(LogEntry e)
    {
        if (e.EventId != 2013) return null;
        string? observer, target; double? like, attraction;
        if (e.Payload is { } p)
        {
            observer = Lower(p.S("Observer")); target = Lower(p.S("Target")); like = Js.Num(p.Get("Like")); attraction = Js.Num(p.Get("Attraction"));
        }
        else
        {
            var m = Js.Match(e.Message, OneWayRe);
            observer = m?[1]?.ToLowerInvariant(); target = m?[2]?.ToLowerInvariant(); like = m?.D(3); attraction = m?.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 2013, ["worldTime"] = e.WorldTimeText, ["observer"] = observer, ["target"] = target,
            ["like"] = like, ["attraction"] = attraction, ["partnerId"] = target ?? Lower(e.RelatedPersonId),
            ["icon"] = "👁️", ["label"] = "One-way observation", ["level"] = "secondary", ["kind"] = "one_way",
        };
    }

    public static JsonObject? ParseInvestment(LogEntry e)
    {
        if (e.EventId != 2014) return null;
        double? commitment, target, investment, alternative;
        if (e.Payload is { } p)
        {
            commitment = Js.Num(p.Get("Commitment")); target = Js.Num(p.Get("Target"));
            investment = Js.Num(p.Get("Investment")); alternative = Js.Num(p.Get("Alternative"));
        }
        else
        {
            var m = Js.Match(e.Message, InvestRe);
            if (m == null) return null;
            commitment = m.D(1); target = m.D(2); investment = m.D(3); alternative = m.D(4);
        }
        string F(double? v) => v == null ? "NaN" : Js.Fmt(v.Value);
        return new JsonObject
        {
            ["eventId"] = 2014, ["worldTime"] = e.WorldTimeText, ["partnerId"] = Lower(e.RelatedPersonId),
            ["commitment"] = commitment, ["target"] = target, ["investment"] = investment, ["alternative"] = alternative,
            ["icon"] = "🔗", ["label"] = $"Investment: commitment {F(commitment)} (alt {F(alternative)})", ["level"] = "info", ["kind"] = "investment",
        };
    }

    public static JsonObject? ParseSocialComparison(LogEntry e)
    {
        if (e.EventId != 2016) return null;
        string target; string? direction, reaction, envy; double? se, mv, mb, am, th;
        if (e.Payload is { } p)
        {
            target = Lower(p.S("Target")); direction = p.S("Direction"); reaction = p.S("Reaction"); envy = p.S("Envy");
            se = Js.Num(p.Get("SelfEsteemDelta")); mv = Js.Num(p.Get("MoodValenceDelta")); mb = Js.Num(p.Get("MoodBaselineDelta"));
            am = Js.Num(p.Get("AchievementMotivationDelta")); th = Js.Num(p.Get("TargetHostilityDelta"));
        }
        else
        {
            var m = Js.Match(e.Message, SocialCompareRe);
            if (m == null) return null;
            target = m[1]!.ToLowerInvariant(); direction = m[2]; reaction = m[3]; envy = m[4];
            se = m.D(5); mv = m.D(6); mb = m.D(7); am = m.D(8); th = m.D(9);
        }
        var envyLabel = envy == "Malicious" ? " [malicious envy]" : envy == "Benign" ? " [benign envy]" : "";
        var icon = direction == "Upward" ? (envy == "Malicious" ? "😤" : "⬆️") : direction == "Downward" ? "⬇️" : "↔️";
        return new JsonObject
        {
            ["eventId"] = 2016, ["worldTime"] = e.WorldTimeText, ["partnerId"] = target, ["direction"] = direction,
            ["reaction"] = reaction, ["envy"] = envy, ["selfEsteemDelta"] = se, ["moodValenceDelta"] = mv,
            ["moodBaselineDelta"] = mb, ["achievementMotivationDelta"] = am, ["targetHostilityDelta"] = th,
            ["icon"] = icon, ["label"] = $"{direction} comparison ({reaction}){envyLabel}",
            ["level"] = envy == "Malicious" ? "danger" : envy == "Benign" ? "success" : "info", ["kind"] = "social_comparison",
        };
    }

    public static JsonObject? ParseReputationPrior(LogEntry e)
    {
        if (e.EventId != 2017) return null;
        string observer, subject; double? prior, shift;
        if (e.Payload is { } p)
        {
            observer = Lower(p.S("Observer")); subject = Lower(p.S("Subject"));
            prior = Js.Num(p.Get("ReputationPrior")); shift = Js.Num(p.Get("TrustShift"));
        }
        else
        {
            var m = Js.Match(e.Message, RepPriorRe);
            if (m == null) return null;
            observer = m[1]!.ToLowerInvariant(); subject = m[2]!.ToLowerInvariant(); prior = m.D(3); shift = m.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 2017, ["worldTime"] = e.WorldTimeText, ["partnerId"] = subject, ["observer"] = observer, ["subject"] = subject,
            ["reputationPrior"] = prior, ["trustShift"] = shift,
            ["icon"] = shift >= 0 ? "✅" : "⚠️",
            ["label"] = $"Reputation prior: prior={(prior != null ? Js.Fx(prior, 3) : "?")} trustShift={(shift != null ? Js.Fx(shift, 1) : "?")}",
            ["level"] = shift != null && shift < -5 ? "danger" : shift != null && shift > 5 ? "success" : "secondary",
            ["kind"] = "reputation_prior",
        };
    }

    private static readonly string[] StateKeys =
    {
        "like", "trust", "closeness", "comfort", "respect", "intimateAffinity", "sexualInterest", "familiarity",
        "aestheticAttraction", "physicalAttraction", "communalStrength", "exchangeStrength", "transgressionResidue",
        "responsiveDesire", "perceivedDominance", "perceivedPrestige",
    };

    /// <summary>Per-partner dimension timeline from 2005 edge snapshots merged with 2002 applied changes.</summary>
    public static Dictionary<string, List<JsonObject>> BuildTimeline(IEnumerable<JsonObject> edges, IEnumerable<JsonObject> applies)
    {
        var all = edges.Select(x => (Edge: true, Ev: x)).Concat(applies.Select(x => (Edge: false, Ev: x)))
            .OrderBy(x => x.Ev.S("worldTime") ?? "", StringComparer.Ordinal).ToList();

        var states = new Dictionary<string, double?[]>();
        var timelines = new Dictionary<string, List<JsonObject>>();
        int Ix(string k) => Array.IndexOf(StateKeys, k);

        foreach (var (isEdge, ev) in all)
        {
            var partner = isEdge ? ev.S("to") : ev.S("relatedPersonId");
            if (string.IsNullOrEmpty(partner)) continue;
            if (!states.TryGetValue(partner, out var st)) { st = new double?[StateKeys.Length]; states[partner] = st; }
            if (!timelines.TryGetValue(partner, out var tl)) { tl = new List<JsonObject>(); timelines[partner] = tl; }

            if (isEdge)
            {
                foreach (var k in new[] { "like", "trust", "closeness", "comfort", "respect", "familiarity", "intimateAffinity",
                                          "sexualInterest", "aestheticAttraction", "physicalAttraction" })
                    st[Ix(k)] = ev.D(k);
                foreach (var k in new[] { "communalStrength", "exchangeStrength", "transgressionResidue", "responsiveDesire",
                                          "perceivedDominance", "perceivedPrestige" })
                    if (ev.D(k) is { } v) st[Ix(k)] = v;
            }
            else
            {
                var d = ev.Obj("dims");
                void Set(string stateKey, string dimKey) { if (d.D(dimKey) is { } v) st[Ix(stateKey)] = v; }
                Set("like", "like"); Set("trust", "trust"); Set("closeness", "closeness"); Set("comfort", "comfort"); Set("respect", "respect");
                if (d.D("intimateaffinity") != null) Set("intimateAffinity", "intimateaffinity");
                else if (d.D("romanticinterest") != null) Set("intimateAffinity", "romanticinterest");
                Set("sexualInterest", "sexualinterest"); Set("familiarity", "familiarity");
                Set("aestheticAttraction", "aestheticattraction"); Set("physicalAttraction", "physicalattraction");
            }

            var point = new JsonObject { ["worldTime"] = ev.Raw("worldTime") };
            for (var i = 0; i < StateKeys.Length; i++) point[StateKeys[i]] = st[i];
            tl.Add(point);
        }
        return timelines;
    }

    public static string PairKey(string? from, string? to) => $"{(from ?? "").ToLowerInvariant()}|{(to ?? "").ToLowerInvariant()}";

    private static readonly string[] PairFields =
    {
        "like", "trust", "closeness", "comfort", "respect", "familiarity", "intimateAffinity", "sexualInterest", "aestheticAttraction",
        "physicalAttraction", "communalStrength", "exchangeStrength", "transgressionResidue", "responsiveDesire", "perceivedDominance",
        "perceivedPrestige", "contemptDestroyed", "commitment", "investmentSize", "alternativeQuality", "dissolutionConsidered",
    };

    public static List<JsonObject> BuildPairSeries(IEnumerable<JsonObject> edges, string from, string to)
    {
        var key = PairKey(from, to);
        return edges.Where(e => PairKey(e.S("from"), e.S("to")) == key)
            .OrderBy(e => e.S("worldTime") ?? "", StringComparer.Ordinal)
            .Select(e =>
            {
                var o = new JsonObject { ["worldTime"] = e.Raw("worldTime") };
                foreach (var f in PairFields) o[f] = e.Raw(f);
                return o;
            }).ToList();
    }

    public static List<JsonObject> ListKnownPairs(IEnumerable<JsonObject> edges, string personId)
    {
        var pid = personId.ToLowerInvariant();
        var seen = new HashSet<string>();
        var pairs = new List<JsonObject>();
        foreach (var e in edges)
        {
            if (e.S("from") != pid) continue;
            if (!seen.Add(PairKey(e.S("from"), e.S("to")))) continue;
            pairs.Add(new JsonObject { ["from"] = e.S("from"), ["to"] = e.S("to") });
        }
        return pairs;
    }

    /// <summary>Weighted Rusbult-style breakup-risk heuristic (not a fitted model).</summary>
    public static JsonObject? ComputeBreakupRisk(IReadOnlyList<JsonObject> series)
    {
        if (series.Count == 0) return null;
        var last = series[^1];
        if (last.D("commitment") == null && last.D("alternativeQuality") == null &&
            last.D("transgressionResidue") == null && last.D("investmentSize") == null) return null;

        var commitment = last.D("commitment") ?? 0;
        var alt = last.D("alternativeQuality") ?? 0;
        var residue = last.D("transgressionResidue") ?? 0;
        var investment = last.D("investmentSize") ?? 0;

        var tail = series.Skip(Math.Max(0, series.Count - 5)).Where(p => p.D("closeness") != null).ToList();
        double cooling = 0;
        if (tail.Count >= 2)
        {
            var delta = tail[^1].D("closeness")!.Value - tail[0].D("closeness")!.Value;
            cooling = Math.Max(0, Math.Min(100, -delta * 4));
        }

        var score = 0.35 * (100 - commitment) + 0.20 * alt + 0.20 * residue + 0.15 * (100 - investment) + 0.10 * cooling;
        var dissolution = last.B("dissolutionConsidered") == true;
        var contempt = last.B("contemptDestroyed") == true;
        var flat = (dissolution ? 15 : 0) + (contempt ? 20 : 0);
        var risk = Math.Max(0, Math.Min(100, score + flat));
        return new JsonObject
        {
            ["risk"] = risk, ["commitment"] = commitment, ["alternativeQuality"] = alt, ["transgressionResidue"] = residue,
            ["investmentSize"] = investment, ["coolingScore"] = cooling,
            ["dissolutionConsidered"] = dissolution, ["contemptDestroyed"] = contempt,
        };
    }
}
