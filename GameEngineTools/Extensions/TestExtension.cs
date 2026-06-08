// TestExtension.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Extensions
{
    using System.Collections;
    using System.Text;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation.Portraits;

    /// <summary>Debugging/diagnostic extension helpers for characters.</summary>
    public static class TestExtension
    {
        /// <summary>Invokes the caster's magic against each target.</summary>
        /// <param name="caster">The casting character.</param>
        /// <param name="tagrets">The targets to affect.</param>
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

        /// <summary>Builds a human-readable info dump for a character.</summary>
        /// <param name="nppc">The character.</param>
        /// <param name="basicInfo">When <c>true</c>, includes only identity basics; otherwise dumps the full snapshot.</param>
        /// <param name="withDNA">When <c>true</c>, prepends the character id.</param>
        public static string PrintInfo(this CharacterBase nppc, bool basicInfo = true, bool withDNA = false)
        {
            var person = nppc.Person;
            var identity = person.Identity;
            var sb = new StringBuilder();

            // --- Basic info ---
            var firstName = identity.FirstName;
            var surname = person.Biology == SexBiology.Female
                ? identity.LastName.Female
                : identity.LastName.Male;

            var displayName = firstName.Familiar.FirstOrDefault(f => !string.IsNullOrEmpty(f))
                ?? firstName.Original;

            if (withDNA) sb.AppendLine(person.Id.Value.ToString());
            sb.AppendLine($"Name: {displayName} {surname}");
            sb.AppendLine($"Born in year: {identity.BirthDate.Year}");
            sb.AppendLine($"Gender: {person.Biology}");

            if (basicInfo)
            {
                return sb.ToString();
            }

            sb.AppendLine();

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

        /// <summary>Builds the portrait-generation prompt info for a character.</summary>
        /// <param name="nppc">The character.</param>
        /// <param name="builder">Portrait spec builder.</param>
        /// <param name="formatter">Portrait prompt formatter.</param>
        public static string PrintPortraitInfo(this CharacterBase nppc, IPortraitSpecBuilder builder, IPortraitPromptFormatter formatter)
        {
            ArgumentNullException.ThrowIfNull(nppc);
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(formatter);

            var portrait = nppc.Person.BuildPortraitSpec(builder);
            var sb = new StringBuilder();
            sb.AppendLine("[PORTRAIT SPEC]");
            AppendValue(sb, portrait, indent: 1);
            sb.AppendLine();
            sb.AppendLine("[PORTRAIT PROMPT]");
            sb.AppendLine(formatter.Format(portrait));
            return sb.ToString();
        }

        private static void AppendValue(StringBuilder sb, object? obj, int indent, HashSet<object>? visited = null)
        {
            if (obj is null)
            {
                return;
            }

            const int maxDepth = 12;
            if (indent > maxDepth)
            {
                sb.AppendLine($"{new string(' ', indent * 2)}[max depth]");
                return;
            }

            var pad = new string(' ', indent * 2);
            var type = obj.GetType();

            // Primitives, string, enum, Guid — print directly
            if (type.IsPrimitive || type.IsEnum || obj is string || obj is Guid || obj is Type)
            {
                sb.AppendLine($"{pad}{obj}");
                return;
            }

            // W-types have a nice ToString() — use it
            if (type.Namespace == "GameEngineTools.World.Utils.Time")
            {
                sb.AppendLine($"{pad}{obj}");
                return;
            }

            // Loop for reference types
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

            // Record / complex object
            foreach (var prop in type.GetProperties())
            {
                if (prop.GetIndexParameters().Length > 0 ||
                    prop.Name == "EqualityContract" ||
                    prop.Name == "DeclaringMethod" ||
                    prop.Name == "ReflectedType")
                {
                    continue;
                }

                var value = prop.GetValue(obj);
                var valueType = value?.GetType();

                bool isSimple = value is null
                    || value is string
                    || value is Guid
                    || value is Type
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
    }
}
