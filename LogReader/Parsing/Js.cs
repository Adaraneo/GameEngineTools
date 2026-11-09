// Parsing/Js.cs
// Small JavaScript-semantics helpers. The log parsers were ported from the Node implementation,
// whose output shape is the contract with the browser UI; these helpers keep the ports line-for-line.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

/// <summary>Result of a regex match; unmatched groups read as null (like JS <c>undefined</c>).</summary>
public sealed class Mt
{
    private readonly Match _m;
    internal Mt(Match m) => _m = m;

    public string? this[int i] => _m.Groups[i].Success ? _m.Groups[i].Value : null;

    /// <summary>JS <c>num(m[i])</c>.</summary>
    public double? D(int i) => this[i] is { } s ? Js.Num(s) : null;

    /// <summary>JS <c>parseInt(m[i], 10)</c>.</summary>
    public long? I(int i) => this[i] is { } s ? Js.ParseInt(s) : null;
}

public static class Js
{
    private static readonly Regex FloatPrefix =
        new(@"^\s*[+-]?(?:Infinity|\d+\.?\d*(?:[eE][+-]?\d+)?|\.\d+(?:[eE][+-]?\d+)?)", RegexOptions.Compiled);
    private static readonly Regex IntPrefix = new(@"^\s*[+-]?\d+", RegexOptions.Compiled);

    /// <summary>Compiles a case-insensitive regex (JS flag <c>i</c>).</summary>
    public static Regex Re(string pattern, bool ignoreCase = true) =>
        new(pattern, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>JS <c>str.match(re)</c> (non-global).</summary>
    public static Mt? Match(string? s, Regex re)
    {
        if (s == null) return null;
        var m = re.Match(s);
        return m.Success ? new Mt(m) : null;
    }

    /// <summary>JS <c>parseFloat(String(s).replace(',', '.'))</c>; NaN becomes null.</summary>
    public static double? Num(string? s)
    {
        if (s == null) return null;
        return ParseFloat(s.Replace(',', '.'));
    }

    /// <summary>Reads any numeric JsonValue (parsed or created from int/long/double/...) as a double.</summary>
    public static bool TryNumber(JsonNode? n, out double d)
    {
        d = 0;
        if (n is not JsonValue v) return false;
        if (v.TryGetValue<double>(out d)) return true;
        if (v.TryGetValue<long>(out var l)) { d = l; return true; }
        if (v.TryGetValue<int>(out var i)) { d = i; return true; }
        if (v.TryGetValue<decimal>(out var m)) { d = (double)m; return true; }
        if (v.TryGetValue<float>(out var f)) { d = f; return true; }
        if (v.GetValueKind() == JsonValueKind.Number)
            return double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        return false;
    }

    /// <summary>JS <c>num(node)</c> for payload values (numbers pass through, strings are parsed).</summary>
    public static double? Num(JsonNode? n)
    {
        if (n == null) return null;
        if (TryNumber(n, out var d)) return double.IsFinite(d) ? d : null;
        if (n is JsonValue v && v.TryGetValue<string>(out var s)) return Num(s);
        if (n is JsonValue v2 && v2.TryGetValue<bool>(out _)) return null;
        return Num(n.ToString());
    }

    /// <summary>JS <c>parseFloat</c> (prefix parse), NaN becomes null.</summary>
    public static double? ParseFloat(string s)
    {
        var m = FloatPrefix.Match(s);
        if (!m.Success) return null;
        var t = m.Value.Trim();
        if (t.EndsWith("Infinity")) return null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;
    }

    /// <summary>JS <c>parseInt(s, 10)</c>; NaN becomes null.</summary>
    public static long? ParseInt(string? s)
    {
        if (s == null) return null;
        var m = IntPrefix.Match(s);
        return m.Success && long.TryParse(m.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l) ? l : null;
    }

    /// <summary>JS <c>Number(node)</c> for payload values.</summary>
    public static double? NumberOf(JsonNode? n)
    {
        if (n == null) return null;
        if (TryNumber(n, out var d)) return double.IsFinite(d) ? d : null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s))
                return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && double.IsFinite(r) ? r : (s.Trim().Length == 0 ? 0 : null);
            if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
        }
        return null;
    }

    /// <summary>JS <c>String(node)</c> for scalars (null stays null).</summary>
    public static string? Str(JsonNode? n)
    {
        if (n == null) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
            if (TryNumber(v, out var d)) return Fmt(d);
        }
        return n.ToJsonString();
    }

    /// <summary>JS number-to-string (shortest round-trip, no exponent for normal ranges).</summary>
    public static string Fmt(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture)
                                                  : d.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>JS <c>x.toFixed(n)</c>; null (NaN) prints "NaN".</summary>
    public static string Fx(double? x, int digits) =>
        x is { } v ? v.ToString("F" + digits, CultureInfo.InvariantCulture) : "NaN";

    /// <summary>JS <c>x.toFixed(n)</c> with a leading '+' for non-negative values.</summary>
    public static string Sx(double? x, int digits) =>
        x is { } v ? (v >= 0 ? "+" : "") + v.ToString("F" + digits, CultureInfo.InvariantCulture) : "NaN";

    /// <summary>JS <c>Math.round</c> (round half up, towards +infinity).</summary>
    public static double Round(double v) => Math.Floor(v + 0.5);

    /// <summary>Ordinal string comparison (JS <c>&lt;</c> on strings compares UTF-16 code units).</summary>
    public static int Cmp(string? a, string? b) => string.CompareOrdinal(a ?? "", b ?? "");

    /// <summary>Copies all properties of <paramref name="src"/> onto <paramref name="dst"/> (JS spread).</summary>
    public static JsonObject Spread(this JsonObject dst, JsonObject? src)
    {
        if (src == null) return dst;
        foreach (var kv in src) dst[kv.Key] = kv.Value?.DeepClone();
        return dst;
    }

    /// <summary>Shallow clone (JS <c>{ ...o }</c>).</summary>
    public static JsonObject Clone(this JsonObject o) => new JsonObject().Spread(o);

    // ── JsonObject read helpers (payload / generic JSON) ─────────────────────────────────

    public static JsonNode? Get(this JsonObject? o, string key) => o != null && o.TryGetPropertyValue(key, out var v) ? v : null;
    public static string? S(this JsonObject? o, string key) => Str(o.Get(key));
    public static double? D(this JsonObject? o, string key) => Num(o.Get(key));
    public static long? L(this JsonObject? o, string key) =>
        NumberOf(o.Get(key)) is { } d ? (long)Math.Truncate(d) : null;
    public static bool? B(this JsonObject? o, string key) =>
        o.Get(key) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    public static JsonObject? Obj(this JsonObject? o, string key) => o.Get(key) as JsonObject;
    public static JsonArray? Arr(this JsonObject? o, string key) => o.Get(key) as JsonArray;

    /// <summary>Case-insensitive key lookup.</summary>
    public static JsonNode? CiGet(this JsonObject? o, string key)
    {
        if (o == null) return null;
        foreach (var kv in o)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    /// <summary>First non-null of several candidate keys (JS <c>a.X ?? a.x</c>).</summary>
    public static JsonNode? Any(this JsonObject? o, params string[] keys)
    {
        if (o == null) return null;
        foreach (var k in keys)
            if (o.TryGetPropertyValue(k, out var v) && v != null) return v;
        return null;
    }

    /// <summary>True when the node is a JSON object / non-null.</summary>
    public static bool IsObj(JsonNode? n) => n is JsonObject;

    /// <summary>Property value cloned for re-parenting into a new object.</summary>
    public static JsonNode? Raw(this JsonObject? o, string key) => o.Get(key)?.DeepClone();

    /// <summary>JS <c>v === true || String(v).toLowerCase() === 'true'</c>.</summary>
    public static bool IsTrue(JsonNode? n) => Str(n)?.ToLowerInvariant() == "true";

    /// <summary>JS <c>typeof v === 'number' ? v : parseInt(v, 10)</c>.</summary>
    public static double? ToInt(JsonNode? n) =>
        TryNumber(n, out var d) ? d : ParseInt(Str(n));

    // ── Collections ───────────────────────────────────────────────────────────────────────

    public static JsonArray ToArray(IEnumerable<JsonNode?> items)
    {
        var a = new JsonArray();
        foreach (var i in items) a.Add(i);
        return a;
    }

    /// <summary>Converts a list of objects to a JSON array (no cloning; the caller owns the objects).</summary>
    public static JsonArray ToArray(IEnumerable<JsonObject> items)
    {
        var a = new JsonArray();
        foreach (var i in items) a.Add(i);
        return a;
    }

    /// <summary>Reads a persisted array back into a list of objects.</summary>
    public static List<JsonObject> ToList(JsonNode? n)
    {
        var l = new List<JsonObject>();
        if (n is JsonArray a)
            foreach (var i in a) if (i is JsonObject o) l.Add((JsonObject)o.DeepClone());
        return l;
    }

    /// <summary>JS string-or-number sort key: world time text compared ordinally.</summary>
    public static int ByWorldTime(JsonObject a, JsonObject b) => Cmp(a.S("worldTime"), b.S("worldTime"));
}
