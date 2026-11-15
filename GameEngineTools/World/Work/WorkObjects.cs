// WorkObjects.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using GameEngineTools.World.Utils.Time;

    /// <summary>The tool classes the built-in tasks ask for; a type names the classes it serves as in <c>objecttypes.json</c>.</summary>
    public static class ToolClasses
    {
        /// <summary>Digging and turning soil by hand (ploughing).</summary>
        public const string Dig = "dig";

        /// <summary>Cutting standing grain (reaping).</summary>
        public const string CutGrain = "cut_grain";

        /// <summary>Beating out grain (threshing).</summary>
        public const string Thresh = "thresh";
    }

    /// <summary>Making instances of catalog types and asking what is at hand.</summary>
    public static class WorkObjects
    {
        /// <summary>
        /// Makes an object of a type: name, category and weight from the catalog, the type id for the world database. Instances are not pickable yet: in this phase a tool is present
        /// at a place and not carried (carrying is the work system's later phase), and a pickable object would be used up by the interaction engine.
        /// </summary>
        /// <exception cref="ArgumentException">The catalog has no such type.</exception>
        public static WorldObject Spawn(WorkCatalog catalog, string typeId, string id, string locationId, HumanId? owner = null)
        {
            var type = catalog.Type(typeId) ?? throw new ArgumentException($"Unknown object type '{typeId}'.", nameof(typeId));
            return new WorldObject
            {
                Id = id,
                DisplayName = type.Name,
                Category = type.Category,
                LocationId = locationId,
                IsAvailable = true,
                IsPickable = false,
                WeightGrams = catalog.WeightGrams(typeId),
                TypeId = typeId,
                OwnerId = owner,
                Affordances = Affordances(type),
            };
        }

        /// <summary>
        /// A fresh, carried item of a type, as a recipe or a harvest makes it: pickable as the type says, held by the character, its spoilage clock started at <paramref name="producedAt"/>,
        /// with the type's affordances (food and drink satisfy hunger and thirst).
        /// </summary>
        public static WorldObject CreateItem(WorkCatalog catalog, string typeId, string locationId, WDateTime producedAt, HumanId? heldBy)
        {
            var type = catalog.Type(typeId) ?? throw new ArgumentException($"Unknown object type '{typeId}'.", nameof(typeId));
            return Spawn(catalog, typeId, $"{typeId}_{Guid.NewGuid():N}", locationId) with { IsPickable = type.Pickable, HeldBy = heldBy, ProducedAt = producedAt };
        }

        /// <summary>True for food that can be eaten as it is; raw ingredients (grain, flour) are Food-category but only ownable.</summary>
        public static bool IsEdible(WorldObject o)
            => o.Category == WorldObjectCategory.Food && o.Affordances.Any(a => a.Type == AffordanceType.Hunger);

        private static System.Collections.Immutable.ImmutableArray<WorldObjectAffordance> Affordances(ObjectTypeDefinition type)
            => type.Affordances is null
                ? System.Collections.Immutable.ImmutableArray<WorldObjectAffordance>.Empty
                : type.Affordances.Select(a => new WorldObjectAffordance(Enum.Parse<AffordanceType>(a.Key), a.Value)).ToImmutableArray();

        /// <summary>Whether something of the given tool class is at hand: among the objects at the place or among those the character holds.</summary>
        public static bool HasTool(WorkCatalog catalog, string toolClass, IEnumerable<WorldObject>? atPlace, IEnumerable<WorldObject>? held)
            => (atPlace ?? Enumerable.Empty<WorldObject>()).Concat(held ?? Enumerable.Empty<WorldObject>()).Any(o => catalog.ToolQuality(o.TypeId, toolClass) > 0.0);
    }
}
