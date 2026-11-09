// Services/NpcLoader.cs — builds character profiles from NPC JSON files (port of npcLoader.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LogReader.Parsing;

namespace LogReader.Services;

public static class NpcLoader
{
    private static readonly Regex UuidRe = new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsUuid(string? s) => s != null && UuidRe.IsMatch(s);

    private static readonly string[] BiologyLabels = { "Female", "Male", "Intersex", "Unknown" };
    private static readonly string[] AttachmentLabels = { "Secure", "Anxious", "Avoidant", "Disorganized" };
    private static readonly string[] CommunicationLabels = { "Direct", "Indirect", "HighContext", "LowContext" };
    private static readonly string[] SociosexualityLabels = { "Restricted", "Intermediate", "Unrestricted" };
    private static readonly string[] ChronotypeLabels = { "Lark", "Neutral", "Owl" };
    private static readonly string[] SkinToneLabels = { "VeryFair", "Fair", "Light", "LightMedium", "Medium", "Tan", "Dark", "VeryDark", "Olive" };
    private static readonly string[] EyeColorLabels = { "Brown", "Hazel", "Green", "Blue", "Gray", "Amber" };
    private static readonly string[] HairColorLabels = { "Black", "DarkBrown", "Brown", "Auburn", "Red", "Blond", "DarkBlond" };
    private static readonly string[] HairTypeLabels = { "Straight", "Wavy", "Curly", "Coily" };
    private static readonly string[] OrientationLabels = { "Heterosexual", "Homosexual", "Bisexual", "Asexual", "Pansexual", "Other" };
    private static readonly string[] FrameLabels = { "None", "Petite", "Medium", "Large" };
    private static readonly string[] BeliefKeys = { "EmotionallySafe", "Warm", "Reliable", "Critical", "Rejecting" };

    /// <summary>Value under the PascalCase key, falling back to the camelCase one (cloned).</summary>
    private static JsonNode? K(JsonObject? o, string pascal) =>
        o.Any(pascal, char.ToLowerInvariant(pascal[0]) + pascal[1..])?.DeepClone();

    private static JsonNode? Kn(JsonObject? o, string pascal) => K(o, pascal);

    private static string? ResolveEnum(JsonNode? val, string[] labels)
    {
        if (val == null) return null;
        if (val is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) { var i = (int)d; return i >= 0 && i < labels.Length ? labels[i] : Js.Fmt(d); }
            if (v.TryGetValue<string>(out var s))
            {
                var n = Js.ParseInt(s);
                if (n is { } k) return k >= 0 && k < labels.Length ? labels[k] : s;
                return s;
            }
        }
        return null;
    }

    private static string? ExtractPersonId(JsonObject obj)
    {
        foreach (var c in new[] { "Id", "PersonId", "HumanId", "CharacterId" })
            if (obj.CiGet(c) is JsonValue v && v.TryGetValue<string>(out var s) && UuidRe.IsMatch(s.Trim()))
                return s.Trim().ToLowerInvariant();
        return null;
    }

    private static string? ExtractBiology(JsonObject obj)
    {
        var raw = obj.Any("Biology", "biology");
        if (raw is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) { var i = (int)d; return i >= 0 && i < BiologyLabels.Length ? BiologyLabels[i] : $"Biology({Js.Fmt(d)})"; }
        if (v.TryGetValue<string>(out var s))
        {
            var n = Js.ParseInt(s);
            if (n is { } k && k >= 0 && k < BiologyLabels.Length) return BiologyLabels[k];
            return s;
        }
        return null;
    }

    private static string? ExtractDisplayName(JsonObject obj, string? biology)
    {
        if (obj.Any("Identity", "identity") is JsonObject identity)
        {
            var fn = identity.Any("FirstName", "firstName");
            string? first = fn is JsonObject fno ? Js.Str(fno.Any("Original", "original")) : Js.Str(fn);

            var ln = identity.Any("LastName", "lastName");
            string? last = null;
            if (ln is JsonObject lno)
            {
                var useFemale = biology is "Female" or "Intersex";
                last = useFemale ? Js.Str(lno.Any("Female", "female", "Male", "male")) : Js.Str(lno.Any("Male", "male", "Female", "female"));
            }
            else if (ln is JsonValue lv && lv.TryGetValue<string>(out var ls)) last = ls;

            if (!string.IsNullOrEmpty(first) || !string.IsNullOrEmpty(last))
            {
                var joined = string.Join(' ', new[] { first, last }.Where(x => !string.IsNullOrEmpty(x)));
                if (joined.Length > 0) return joined;
            }
        }
        return Js.Str(obj.Any("Name", "name", "FullName", "fullName", "DisplayName", "displayName"));
    }

    private static string? ExtractBirthDateText(JsonObject obj)
    {
        if (obj.Any("Identity", "identity") is not JsonObject identity) return null;
        if (identity.Any("BirthDate", "birthDate") is not JsonObject bd) return null;
        var year = bd.Any("Year", "year");
        if (year == null) return null;
        return $"Y{Js.Str(year)} M{Js.Str(bd.Any("Month", "month")) ?? "?"} D{Js.Str(bd.Any("Day", "day")) ?? "?"}";
    }

    private static JsonArray? ExtractFamiliarNames(JsonObject obj)
    {
        if (obj.Any("Identity", "identity") is not JsonObject identity) return null;
        if (identity.Any("FirstName", "firstName") is not JsonObject fn) return null;
        if (fn.Any("Familiar", "familiar") is not JsonArray arr) return null;
        var names = arr.Select(n => (Js.Str(n) ?? "").Trim()).Where(n => n.Length > 0).ToList();
        return names.Count > 0 ? Js.ToArray(names.Select(n => (JsonNode?)n)) : null;
    }

    private static JsonObject? ExtractSurnames(JsonObject obj, string? biology)
    {
        if (obj.Any("Identity", "identity") is not JsonObject identity) return null;
        if (identity.Any("LastName", "lastName") is not JsonObject raw) return null;
        var male = Js.Str(raw.Any("Male", "male"));
        var female = Js.Str(raw.Any("Female", "female"));
        if (string.IsNullOrEmpty(male) && string.IsNullOrEmpty(female)) return null;
        var useFemale = biology is "Female" or "Intersex";
        var own = useFemale ? female : male;
        var other = useFemale ? male : female;
        return new JsonObject { ["own"] = own, ["other"] = own != other ? other : null };
    }

    private static JsonObject? ExtractPersonality(JsonObject obj)
    {
        if (obj.Any("Personality", "personality") is not JsonObject pers) return null;
        var bf = pers.CiGet("BigFive") as JsonObject ?? pers;

        JsonNode? Ci(JsonObject o, string k) => o.CiGet(k)?.DeepClone();
        var openness = Ci(bf, "Openness"); var cons = Ci(bf, "Conscientiousness"); var extra = Ci(bf, "Extraversion");
        var agree = Ci(bf, "Agreeableness"); var neuro = Ci(bf, "Neuroticism") ?? Ci(bf, "NegativeEmotionality");

        string? attachment = null;
        var rawAtt = pers.CiGet("Attachment");
        if (rawAtt != null)
        {
            if (rawAtt is JsonObject ao)
            {
                var anx = Js.Num(ao.Any("Anxiety", "anxiety")) ?? 0;
                var avo = Js.Num(ao.Any("Avoidance", "avoidance")) ?? 0;
                attachment = anx > 0.5 && avo < 0.5 ? "Anxious" : avo > 0.5 && anx < 0.5 ? "Avoidant" : anx > 0.5 && avo > 0.5 ? "Disorganized" : "Secure";
            }
            else attachment = ResolveEnum(rawAtt, AttachmentLabels);
        }
        var communication = ResolveEnum(pers.CiGet("Communication"), CommunicationLabels);
        var sociosexuality = ResolveEnum(pers.CiGet("Sociosexuality"), SociosexualityLabels);
        var chronotype = ResolveEnum(pers.CiGet("Chronotype"), ChronotypeLabels);

        JsonObject? motivation = null;
        if (pers.CiGet("Motivation") is JsonObject mo)
        {
            motivation = new JsonObject();
            foreach (var k in new[] { "Affiliation", "Achievement", "Power", "Altruism", "Competence", "Autonomy", "Curiosity", "Rest", "Sexuality" })
                motivation[char.ToLowerInvariant(k[0]) + k[1..]] = Ci(mo, k);
            if (motivation.All(kv => kv.Value == null)) motivation = null;
        }

        var any = new JsonNode?[] { openness, cons, extra, agree, neuro, attachment, communication, sociosexuality, chronotype, motivation }.Any(v => v != null);
        if (!any) return null;
        return new JsonObject
        {
            ["openness"] = openness, ["conscientiousness"] = cons, ["extraversion"] = extra, ["agreeableness"] = agree, ["neuroticism"] = neuro,
            ["attachment"] = attachment, ["communication"] = communication, ["sociosexuality"] = sociosexuality,
            ["chronotype"] = chronotype, ["motivation"] = motivation,
        };
    }

    private static JsonObject? ExtractAppearance(JsonObject obj)
    {
        if (obj.Any("GeneticBlueprint", "geneticBlueprint") is not JsonObject bp) return null;
        var r = new JsonObject();

        if (bp.CiGet("Colors") is JsonObject colors)
        {
            r["skinTone"] = ResolveEnum(colors.CiGet("SkinTone"), SkinToneLabels);
            r["eyeColor"] = ResolveEnum(colors.CiGet("EyeColor"), EyeColorLabels);
            r["hairColor"] = ResolveEnum(colors.CiGet("HairColor"), HairColorLabels);
            r["hairType"] = ResolveEnum(colors.CiGet("HairType"), HairTypeLabels);
        }
        if (bp.CiGet("BodyLatent") is JsonObject bl)
            foreach (var (outK, inK) in new[] { ("heightCm", "HeightCm"), ("muscularity", "Muscularity"), ("adiposity", "Adiposity"), ("robustness", "Robustness"),
                ("posture", "Posture"), ("softTissueFullness", "SoftTissueFullness"), ("lowerMass", "LowerMass"), ("sexDimorphism", "SexDimorphism"), ("juvenility", "Juvenility") })
                r[outK] = bl.Raw(inK);
        if (bp.CiGet("FaceLatent") is JsonObject fl)
            foreach (var (outK, inK) in new[] { ("faceSoftness", "Softness"), ("faceAngularity", "Angularity"), ("faceRobustness", "Robustness"),
                ("midfaceProjection", "MidfaceProjection"), ("browProminence", "BrowProminence"), ("noseScale", "NoseScale"),
                ("eyeScale", "EyeScale"), ("lipFullness", "LipFullness"), ("faceSexDimorphism", "SexDimorphism") })
                r[outK] = fl.Raw(inK);
        if (obj.Obj("Snapshot").Obj("Physiology").Obj("Aging") is { } aging)
            foreach (var (outK, inK) in new[] { ("hairLengthCm", "HairLengthCm"), ("greyFraction", "GreyFraction"), ("hairDensity", "HairDensity"),
                ("wrinkleScore", "WrinkleScore"), ("ageYears", "AgeYears") })
                r[outK] = aging.Raw(inK);
        if (bp.CiGet("DistinctiveMarks") is JsonArray marks)
        {
            var filtered = marks.Select(m => (Js.Str(m) ?? "").Trim()).Where(m => m.Length > 0).ToList();
            if (filtered.Count > 0) r["distinctiveMarks"] = Js.ToArray(filtered.Select(m => (JsonNode?)m));
        }
        return r.Count > 0 ? r : null;
    }

    private static JsonObject? ExtractAttractionProfile(JsonObject obj)
    {
        if (obj.Any("AttractionProfile", "attractionProfile") is not JsonObject a) return null;
        JsonNode? Ci(string k) => a.CiGet(k)?.DeepClone();
        var r = new JsonObject
        {
            ["orientation"] = ResolveEnum(a.CiGet("Orientation"), OrientationLabels),
            ["preferredHeightCm"] = Ci("PreferredHeightCm"), ["heightToleranceCm"] = Ci("HeightToleranceCm"),
            ["framePreference"] = ResolveEnum(a.CiGet("FramePreference"), FrameLabels),
            ["preferredWhr"] = Ci("PreferredWhr"), ["symmetryWeight"] = Ci("SymmetryWeight"),
            ["femaleAttraction"] = Ci("FemaleTargetAttraction"), ["maleAttraction"] = Ci("MaleTargetAttraction"),
            ["otherAttraction"] = Ci("OtherTargetAttraction"),
        };
        return r.Any(kv => kv.Value != null) ? r : null;
    }

    private static JsonObject? ExtractSnapshot(JsonObject obj)
    {
        if (obj.Any("Snapshot", "snapshot") is not JsonObject snap) return null;

        JsonObject? physiology = null;
        if (snap.Any("Physiology", "physiology") is JsonObject phys)
        {
            var nut = phys.Any("Nutrition", "nutrition") as JsonObject;
            var cycle = phys.Any("Cycle", "cycle") as JsonObject;
            physiology = new JsonObject
            {
                ["energy"] = K(phys, "Energy"), ["sleepDebtHours"] = K(phys, "SleepDebtHours"), ["hunger"] = K(phys, "Hunger"),
                ["thirst"] = K(phys, "Thirst"), ["pain"] = K(phys, "Pain"), ["immuneLoad"] = K(phys, "ImmuneLoad"),
                ["bodyTempDelta"] = K(phys, "BodyTempDelta"), ["allostaticLoad"] = K(phys, "AllostaticLoad"),
                ["cortisolLevel"] = K(phys, "CortisolLevel"), ["cyclePhase"] = cycle != null ? K(cycle, "Phase") : null,
                ["vitaminD"] = K(nut, "VitaminD"), ["calories"] = K(nut, "Calories"), ["iron"] = K(nut, "Iron"),
                ["protein"] = K(nut, "Protein"), ["bloodGlucose"] = K(nut, "BloodGlucoseLevel"),
            };
        }

        JsonObject? psychology = null;
        if (snap.Any("Psychology", "psychology") is JsonObject psych)
        {
            var mot = psych.Any("Motivations", "motivations") as JsonObject;
            psychology = new JsonObject
            {
                ["valence"] = K(psych, "Valence"), ["arousal"] = K(psych, "Arousal"), ["dominance"] = K(psych, "Dominance"),
                ["stress"] = K(psych, "Stress"), ["cognitiveLoad"] = K(psych, "CognitiveLoad"), ["dominantEmotion"] = K(psych, "DominantEmotion"),
                ["moodBaseline"] = K(psych, "MoodBaseline"), ["needSocial"] = K(mot, "NeedSocial"), ["needIntimacy"] = K(mot, "NeedIntimacy"),
                ["needAchievement"] = K(mot, "NeedAchievement"), ["needCare"] = K(mot, "NeedCare"), ["needSafety"] = K(mot, "NeedSafety"),
            };
        }

        JsonObject? behavior = null;
        if (snap.Any("Behavior", "behavior") is JsonObject beh)
        {
            var plan = beh.Any("CurrentPlan", "currentPlan") as JsonObject;
            var intent = beh.Any("ActiveIntent", "activeIntent") as JsonObject;

            JsonObject? cooldowns = null;
            if (beh.Any("Cooldowns", "cooldowns") is JsonObject cd)
            {
                var o = new JsonObject();
                foreach (var kv in cd)
                    if (kv.Value is JsonValue v && v.TryGetValue<double>(out var h) && h > 0) o[kv.Key] = h;
                if (o.Count > 0) cooldowns = o;
            }

            var traces = new List<JsonObject>();
            if (beh.Any("HabitTraces", "habitTraces") is JsonObject ht)
                foreach (var t in ht.Select(kv => kv.Value).OfType<JsonObject>())
                {
                    if ((Js.Num(t.Any("Strength", "strength")) ?? 0) <= 0.01) continue;
                    traces.Add(new JsonObject
                    {
                        ["actionName"] = K(t, "ActionName"), ["surfaceKind"] = K(t, "SurfaceKind"), ["timeBand"] = K(t, "TimeBand"),
                        ["cueKind"] = K(t, "CueKind"), ["strength"] = K(t, "Strength"), ["repetitionCount"] = K(t, "RepetitionCount"),
                        ["tendency"] = K(t, "Tendency"), ["adaptiveReinforcement"] = K(t, "AdaptiveReinforcement"),
                    });
                }
            var sorted = traces.OrderByDescending(t => t.D("strength") ?? 0).ToList();

            behavior = new JsonObject
            {
                ["needRest"] = K(beh, "NeedRest"), ["needFood"] = K(beh, "NeedFood"), ["needWater"] = K(beh, "NeedWater"),
                ["needBelonging"] = K(beh, "NeedBelonging"), ["needCompetence"] = K(beh, "NeedCompetence"), ["needIntimacy"] = K(beh, "NeedIntimacy"),
                ["currentPlanName"] = plan != null ? K(plan, "Name") : null, ["currentPlanUtility"] = plan != null ? K(plan, "Utility") : null,
                ["activeIntentKind"] = intent != null ? K(intent, "Kind") : null, ["activeIntentStrength"] = intent != null ? K(intent, "Strength") : null,
                ["activeIntentCommitment"] = intent != null ? K(intent, "Commitment") : null,
                ["cooldowns"] = cooldowns, ["habitTraces"] = Js.ToArray(sorted),
            };
        }

        JsonObject? surface = null;
        if (snap.Any("InteractionSurface", "interactionSurface") is JsonObject surf)
            surface = new JsonObject { ["location"] = K(surf, "Location"), ["hasPrivacy"] = K(surf, "HasPrivacy"), ["noise"] = K(surf, "Noise"), ["crowding"] = K(surf, "Crowding") };

        var relationships = new JsonObject();
        if (snap.Any("Relationships", "relationships") is JsonObject relSnap)
        {
            var edges = (relSnap.Any("Edges", "edges") as JsonObject) ?? relSnap;
            foreach (var (pid, node) in edges)
            {
                if (!UuidRe.IsMatch(pid) || node is not JsonObject edge) continue;
                relationships[pid.ToLowerInvariant()] = new JsonObject
                {
                    ["like"] = K(edge, "Like"), ["trust"] = K(edge, "Trust"), ["closeness"] = K(edge, "Closeness"), ["comfort"] = K(edge, "Comfort"),
                    ["respect"] = K(edge, "Respect"),
                    ["intimateAffinity"] = edge.Any("IntimateAffinity", "intimateAffinity", "RomanticInterest", "romanticInterest")?.DeepClone(),
                    ["sexualInterest"] = K(edge, "SexualInterest"), ["familiarity"] = K(edge, "Familiarity"),
                    ["aestheticAttraction"] = K(edge, "AestheticAttraction"), ["physicalAttraction"] = K(edge, "PhysicalAttraction"),
                    ["transgressionResidue"] = K(edge, "TransgressionResidue"), ["lastContactTime"] = K(edge, "LastContactTime"),
                    ["isContemptuouslyDestroyed"] = K(edge, "IsContemptuouslyDestroyed") ?? false,
                };
            }
        }

        var semanticMemory = new JsonObject();
        if (snap.Any("SemanticMemory", "semanticMemory") is JsonObject semSnap)
        {
            var people = (semSnap.Any("People", "people") as JsonObject) ?? semSnap;
            foreach (var (pid, node) in people)
            {
                if (!UuidRe.IsMatch(pid) || node is not JsonObject entry) continue;
                var beliefs = (entry.Any("Beliefs", "beliefs") as JsonObject) ?? entry;
                var mem = new JsonObject();
                foreach (var key in BeliefKeys)
                {
                    if (beliefs.Any(key, key.ToLowerInvariant()) is not JsonObject belief) continue;
                    var strength = Js.Num(belief.Any("Strength", "strength")) ?? 0;
                    var evidence = Js.Num(belief.Any("EvidenceCount", "evidenceCount")) ?? 0;
                    if (strength > 0.3 && evidence > 0) mem[char.ToLowerInvariant(key[0]) + key[1..]] = strength;
                }
                if (mem.Count > 0) semanticMemory[pid.ToLowerInvariant()] = mem;
            }
        }

        return new JsonObject
        {
            ["physiology"] = physiology, ["psychology"] = psychology, ["behavior"] = behavior,
            ["interactionSurface"] = surface, ["relationships"] = relationships, ["semanticMemory"] = semanticMemory,
        };
    }

    private static IEnumerable<string> CollectJsonFiles(string dir)
    {
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(dir); } catch { yield break; }
        foreach (var e in entries)
        {
            if (Directory.Exists(e)) { foreach (var f in CollectJsonFiles(e)) yield return f; }
            else if (e.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) yield return e;
        }
    }

    /// <summary>Loads every NPC profile under <paramref name="npcRoot"/>, keyed by lowercase person id.</summary>
    public static Dictionary<string, JsonObject> LoadAll(string npcRoot, ILogger log)
    {
        var map = new Dictionary<string, JsonObject>();
        foreach (var path in CollectJsonFiles(npcRoot))
        {
            JsonObject? obj;
            try { obj = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
            catch (Exception ex) { log.LogWarning("[npcLoader] Cannot read {Path}: {Msg}", path, ex.Message); continue; }
            if (obj == null) continue;

            var id = ExtractPersonId(obj);
            if (id == null) { log.LogWarning("[npcLoader] No valid personId in {Path}", path); continue; }

            var biology = ExtractBiology(obj);
            map[id] = new JsonObject
            {
                ["id"] = id, ["displayName"] = ExtractDisplayName(obj, biology) ?? id, ["familiarNames"] = ExtractFamiliarNames(obj),
                ["surnames"] = ExtractSurnames(obj, biology), ["birthDateText"] = ExtractBirthDateText(obj), ["biology"] = biology,
                ["personality"] = ExtractPersonality(obj), ["appearance"] = ExtractAppearance(obj),
                ["attractionProfile"] = ExtractAttractionProfile(obj), ["snapshot"] = ExtractSnapshot(obj), ["hasNpcData"] = true,
            };
        }
        log.LogInformation("[npcLoader] Loaded {Count} NPC profiles from {Root}", map.Count, npcRoot);
        return map;
    }
}
