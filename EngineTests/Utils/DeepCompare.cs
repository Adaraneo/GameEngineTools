// DeepCompare.cs
// Copyright (c) 50PSoftware

namespace EngineTests.Utils
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    /// <summary>Reflection-based structural comparison of two object graphs; reports the path of every difference.</summary>
    internal static class DeepCompare
    {
        /// <summary>Compares <paramref name="a"/> with <paramref name="b"/> and appends one line per difference to <paramref name="diffs"/>.</summary>
        /// <param name="ignoreGuids">Skip <see cref="Guid"/> values (identifiers that are random by construction).</param>
        public static void Compare(string path, object? a, object? b, List<string> diffs, bool ignoreGuids = false)
            => Walk(path, a, b, diffs, 0, ignoreGuids);

        private static void Walk(string path, object? a, object? b, List<string> diffs, int depth, bool ignoreGuids)
        {
            if (ReferenceEquals(a, b)) return;
            if (a is null || b is null) { diffs.Add($"{path}: {Show(a)} vs {Show(b)}"); return; }
            if (depth > 14) return;

            var type = a.GetType();
            var collections = a is IEnumerable && b is IEnumerable && a is not string; // an array vs a List is the same data
            if (!collections && type != b.GetType()) { diffs.Add($"{path}: type {type.Name} vs {b.GetType().Name}"); return; }

            if (a is double da)
            {
                var db = (double)b;
                if (!(Math.Abs(da - db) <= 1e-9 * Math.Max(1.0, Math.Abs(da))) && !(double.IsNaN(da) && double.IsNaN(db)))
                    diffs.Add($"{path}: {da:R} vs {db:R}");
                return;
            }

            if (type.IsPrimitive || type.IsEnum || a is string || a is decimal || a is Guid)
            {
                if (a is Guid && ignoreGuids) return;
                if (!a.Equals(b)) diffs.Add($"{path}: {Show(a)} vs {Show(b)}");
                return;
            }

            if (a is IDictionary dictA && b is IDictionary dictB)
            {
                if (dictA.Count != dictB.Count) { diffs.Add($"{path}: dictionary count {dictA.Count} vs {dictB.Count}"); return; }
                foreach (var key in dictA.Keys)
                {
                    if (!dictB.Contains(key)) { diffs.Add($"{path}[{Show(key)}]: missing after import"); continue; }
                    Walk($"{path}[{Show(key)}]", dictA[key], dictB[key], diffs, depth + 1, ignoreGuids);
                }

                return;
            }

            if (a is IEnumerable enumA && b is IEnumerable enumB)
            {
                var listA = enumA.Cast<object?>().ToList();
                var listB = enumB.Cast<object?>().ToList();
                if (listA.Count != listB.Count) { diffs.Add($"{path}: count {listA.Count} vs {listB.Count}"); return; }
                for (var i = 0; i < listA.Count; i++)
                    Walk($"{path}[{i}]", listA[i], listB[i], diffs, depth + 1, ignoreGuids);
                return;
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && !typeof(Delegate).IsAssignableFrom(p.PropertyType))
                .ToList();
            if (props.Count == 0)
            {
                if (!a.Equals(b)) diffs.Add($"{path}: {Show(a)} vs {Show(b)}");
                return;
            }

            foreach (var p in props)
            {
                object? va, vb;
                try { va = p.GetValue(a); vb = p.GetValue(b); }
                catch { continue; }
                Walk($"{path}.{p.Name}", va, vb, diffs, depth + 1, ignoreGuids);
            }
        }

        private static string Show(object? o) => o is null ? "null" : o.ToString() ?? o.GetType().Name;
    }
}
