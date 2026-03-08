// TestExtension.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Extensions
{
    using System.Collections;
    using System.Text;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.World.Core.Time;

    public static class TestExtension
    {
        public static List<CharacterBase> CreateFamilyForPlayer(this GameEngineToolsManager instance, PC player)
        {
            throw new NotImplementedException();
        }

        public static void DoMagic(this CharacterBase caster, params object[] tagrets)
        {
            if (caster != null)
            {
                foreach (var target in tagrets)
                {
                    caster.DoMagic(target);
                }
            }
        }

        #region ByClaude

        public static string PrintInfo(this CharacterBase nppc, WorldTimeContext wtctx, bool basicInfo = true, bool withDNA = false)
        {
            var person = nppc.Person;
            var identity = person.Identity;
            var sb = new StringBuilder();

            // --- Základní info ---
            var firstName = identity.FirstName;
            var surname = person.Biology == SexBiology.Female
                ? identity.LastName.Female
                : identity.LastName.Male;

            var displayName = firstName.Familiar.FirstOrDefault(f => !string.IsNullOrEmpty(f))
                ?? firstName.Original;

            sb.AppendLine($"Name: {displayName} {surname}");
            var (year, _, _) = wtctx.GetDateParts(identity.BirthDate);
            sb.AppendLine($"Born in year: {year}");

            if (basicInfo)
            {
                return sb.ToString();
            }

            // --- Snapshot ---
            sb.AppendLine();
            foreach (var snapshotProp in person.Snapshot.GetType().GetProperties())
            {
                sb.AppendLine($"[{snapshotProp.Name.ToUpperInvariant()}]");
                var stateObj = snapshotProp.GetValue(person.Snapshot);
                AppendValue(sb, stateObj, indent: 1);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void AppendValue(StringBuilder sb, object? obj, int indent, HashSet<object>? visited = null)
        {
            if (obj is null)
            {
                return;
            }

            var pad = new string(' ', indent * 2);
            var type = obj.GetType();

            // Primitivy, string, enum, Guid — rovnou vypíšeme
            if (type.IsPrimitive || type.IsEnum || obj is string || obj is Guid)
            {
                sb.AppendLine($"{pad}{obj}");
                return;
            }

            // ✅ Tvoje W-typy mají hezký ToString() — použijeme ho
            if (type.Namespace == "GameEngineTools.World.Utils.Time")
            {
                sb.AppendLine($"{pad}{obj}");
                return;
            }

            // Cyklus pro referenční typy
            if (!type.IsValueType)
            {
                visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
                if (!visited.Add(obj))
                {
                    sb.AppendLine($"{pad}[circular reference]");
                    return;
                }
            }

            // Dictionary
            if (obj is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    sb.AppendLine($"{pad}[{entry.Key}]");
                    AppendValue(sb, entry.Value, indent + 1, visited);
                }
                return;
            }

            // Kolekce
            if (obj is IEnumerable enumerable && obj is not string)
            {
                int i = 0;
                foreach (var item in enumerable)
                {
                    sb.AppendLine($"{pad}[{i++}]");
                    AppendValue(sb, item, indent + 1, visited);
                }
                return;
            }

            // Record / komplexní objekt
            foreach (var prop in type.GetProperties())
            {
                var value = prop.GetValue(obj);
                var valueType = value?.GetType();

                bool isSimple = value is null
                    || value is string
                    || value is Guid
                    || (valueType?.IsPrimitive ?? false)
                    || (valueType?.IsEnum ?? false)
                    || valueType?.Namespace == "GameEngineTools.World.Utils.Time";

                if (isSimple)
                {
                    sb.AppendLine($"{pad}{prop.Name}: {value ?? "null"}");
                }
                else
                {
                    sb.AppendLine($"{pad}{prop.Name}:");
                    AppendValue(sb, value, indent + 1, visited);
                }
            }
        }

        #endregion
    }
}
