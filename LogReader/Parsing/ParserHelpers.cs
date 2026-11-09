// Parsing/ParserHelpers.cs
using System.Text.Json.Nodes;

namespace LogReader.Parsing;

/// <summary>Shared regex fragments and lookup tables (port of parsers/helpers.js).</summary>
public static class P
{
    /// <summary>Signed decimal; .NET may format floats with ',' under Czech locale.</summary>
    public const string N = @"[+-]?\d+(?:[.,]\d+)?";
    public const string NU = @"\d+(?:[.,]\d+)?";
    public const string Arr = "(?:→|->)";

    public static string? NormalizeOutcome(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var r = raw.ToLowerInvariant();
        return r is "přijato" or "accepted" or "true" ? "accepted" : "rejected";
    }

    public static readonly Dictionary<string, string> EmotionIcons = new()
    {
        ["Neutral"] = "😐", ["Joy"] = "😄", ["Sadness"] = "😢", ["Anger"] = "😠", ["Fear"] = "😨",
        ["Disgust"] = "🤢", ["Surprise"] = "😲", ["Tenderness"] = "🥰", ["Pride"] = "😤", ["Shame"] = "😳",
    };

    public readonly record struct ActionMeta(string Icon, string Label, string Level);

    private static readonly Dictionary<string, ActionMeta> AllActionMeta = new()
    {
        ["Eat"] = new("🍽️", "Eat", "success"),
        ["Drink"] = new("💧", "Drink", "info"),
        ["SelfCare"] = new("🧼", "Self-care", "primary"),
        ["Sleep"] = new("😴", "Sleep", "secondary"),
        ["MoveToRest"] = new("🛏️", "Move to rest", "secondary"),
        ["ReachOut"] = new("🤝", "Reach out", "warning"),
        ["InviteIntimacy"] = new("💕", "Invite intimacy", "danger"),
        ["Idle"] = new("💭", "Idle", "light"),
        ["Create"] = new("🎨", "Create", "info"),
        ["Work"] = new("⚒️", "Work", "dark"),
    };

    public static IEnumerable<string> ActionKeys => AllActionMeta.Keys;

    /// <summary>ALL_ACTION_META[action] ?? fallback (label defaults to the action name).</summary>
    public static ActionMeta Meta(string? action, string fallbackIcon, string fallbackLevel, string? fallbackLabel = null)
    {
        if (action != null && AllActionMeta.TryGetValue(action, out var m)) return m;
        return new(fallbackIcon, fallbackLabel ?? action ?? "", fallbackLevel);
    }

    public static bool HasMeta(string? action) => action != null && AllActionMeta.ContainsKey(action);

    /// <summary>JS <c>{ ...meta }</c>: icon, label, level.</summary>
    public static JsonObject Spread(this JsonObject o, ActionMeta m)
    {
        o["icon"] = m.Icon; o["label"] = m.Label; o["level"] = m.Level;
        return o;
    }
}
