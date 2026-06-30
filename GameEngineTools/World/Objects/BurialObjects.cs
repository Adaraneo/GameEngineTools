// BurialObjects.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Factory + identity helpers for the two burial world objects — a <see cref="WorldObjectCategory.Corpse"/>
    /// spawned at the place of death and the <see cref="WorldObjectCategory.Grave"/> it becomes once interred.
    /// </summary>
    /// <remarks>
    /// The deceased character's <see cref="HumanId"/> is encoded directly in the object id so a corpse or
    /// grave can be mapped back to the person it belongs to without a side table.
    /// </remarks>
    public static class BurialObjects
    {
        /// <summary>Id prefix for corpse objects (followed by the deceased's GUID).</summary>
        public const string CorpsePrefix = "corpse:";

        /// <summary>Id prefix for grave objects (followed by the deceased's GUID).</summary>
        public const string GravePrefix = "grave:";

        /// <summary>Builds a corpse object for <paramref name="deceased"/> at the place of death.</summary>
        public static WorldObject Corpse(HumanId deceased, string locationId, string displayName)
            => new()
            {
                Id = CorpsePrefix + deceased.Value,
                DisplayName = displayName,
                Category = WorldObjectCategory.Corpse,
                LocationId = locationId,
                IsAvailable = true,
            };

        /// <summary>Builds a grave object for <paramref name="deceased"/> at the burial location.</summary>
        public static WorldObject Grave(HumanId deceased, string locationId, string displayName)
            => new()
            {
                Id = GravePrefix + deceased.Value,
                DisplayName = displayName,
                Category = WorldObjectCategory.Grave,
                LocationId = locationId,
                IsAvailable = true,
            };

        /// <summary>
        /// Recovers the deceased character's <see cref="HumanId"/> from a corpse or grave object.
        /// Returns <c>false</c> for any other object or a malformed id.
        /// </summary>
        public static bool TryGetDeceased(WorldObject obj, out HumanId deceased)
        {
            deceased = default;

            var prefix = obj.Category switch
            {
                WorldObjectCategory.Corpse => CorpsePrefix,
                WorldObjectCategory.Grave => GravePrefix,
                _ => null
            };

            if (prefix is null || !obj.Id.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            if (Guid.TryParse(obj.Id.AsSpan(prefix.Length), out var guid))
            {
                deceased = new HumanId(guid);
                return true;
            }

            return false;
        }
    }
}
