// WorkCatalog.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    using GameEngineTools.World.Objects;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>What a material is: the class it belongs to and its density, from which the weight of anything made of it follows.</summary>
    /// <param name="Id">Stable id, referenced by object types.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Class">Metal, wood, stone and so on.</param>
    /// <param name="DensityKgM3">Density in kilograms per cubic metre.</param>
    /// <param name="Source">Where the value comes from.</param>
    public sealed record MaterialDefinition(string Id, string Name, MaterialClass Class, double DensityKgM3, string? Source = null);

    /// <summary>Broad class of a material.</summary>
    public enum MaterialClass
    {
        /// <summary>Anything not listed.</summary>
        Other,

        /// <summary>Iron, steel, copper, bronze.</summary>
        Metal,

        /// <summary>Timber.</summary>
        Wood,

        /// <summary>Stone and flint.</summary>
        Stone,

        /// <summary>Leather, hide.</summary>
        Leather,

        /// <summary>Clay and ceramics.</summary>
        Clay,

        /// <summary>Rope, cloth, straw.</summary>
        Fibre,
    }

    /// <summary>One part of an object: a volume of a material.</summary>
    /// <param name="MaterialId">The material.</param>
    /// <param name="VolumeCm3">Volume in cubic centimetres.</param>
    public sealed record ObjectPart(string MaterialId, double VolumeCm3);

    /// <summary>What a kind of object is: its parts (so its weight), what it can be used as, and how long it lasts.</summary>
    /// <param name="Id">Stable id; <c>WorldObject.TypeId</c> in the world database points here.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Category">The world object category of its instances.</param>
    /// <param name="Pickable">Whether it can be carried.</param>
    /// <param name="Parts">Materials and volumes it is made of.</param>
    /// <param name="ToolClasses">Tool class to quality (1 is ordinary) it can serve as.</param>
    /// <param name="DurabilityHours">Hours of use until worn out, or null when it does not wear.</param>
    /// <param name="Source">Where the parts and volumes come from.</param>
    public sealed record ObjectTypeDefinition(
        string Id,
        string Name,
        WorldObjectCategory Category,
        bool Pickable,
        IReadOnlyList<ObjectPart> Parts,
        IReadOnlyDictionary<string, double> ToolClasses,
        double? DurabilityHours = null,
        string? Source = null);

    /// <summary>The materials and object types the work system knows; the world database stores only a type id and the state of an instance, this holds what the type is.</summary>
    public sealed class WorkCatalog
    {
        private readonly Dictionary<string, MaterialDefinition> _materials;
        private readonly Dictionary<string, ObjectTypeDefinition> _types;

        /// <summary>Builds and checks a catalog.</summary>
        /// <exception cref="FormatException">An id is duplicated or empty, a density or volume is not positive, or a part names an unknown material.</exception>
        public WorkCatalog(IEnumerable<MaterialDefinition> materials, IEnumerable<ObjectTypeDefinition> types)
        {
            _materials = new Dictionary<string, MaterialDefinition>(StringComparer.Ordinal);
            foreach (var m in materials)
            {
                if (string.IsNullOrWhiteSpace(m.Id)) throw new FormatException("A material has no id.");
                if (!(m.DensityKgM3 > 0.0)) throw new FormatException($"Material '{m.Id}': density must be positive.");
                if (!_materials.TryAdd(m.Id, m)) throw new FormatException($"Duplicate material '{m.Id}'.");
            }

            _types = new Dictionary<string, ObjectTypeDefinition>(StringComparer.Ordinal);
            foreach (var t in types)
            {
                if (string.IsNullOrWhiteSpace(t.Id)) throw new FormatException("An object type has no id.");
                foreach (var part in t.Parts)
                {
                    if (!_materials.ContainsKey(part.MaterialId)) throw new FormatException($"Object type '{t.Id}': unknown material '{part.MaterialId}'.");
                    if (!(part.VolumeCm3 > 0.0)) throw new FormatException($"Object type '{t.Id}': the volume of '{part.MaterialId}' must be positive.");
                }

                foreach (var (toolClass, quality) in t.ToolClasses)
                    if (!(quality > 0.0)) throw new FormatException($"Object type '{t.Id}': tool class '{toolClass}' needs a positive quality.");
                if (t.DurabilityHours is { } d && !(d > 0.0)) throw new FormatException($"Object type '{t.Id}': durability must be positive.");
                if (!_types.TryAdd(t.Id, t)) throw new FormatException($"Duplicate object type '{t.Id}'.");
            }
        }

        /// <summary>All materials.</summary>
        public IReadOnlyCollection<MaterialDefinition> Materials => _materials.Values;

        /// <summary>All object types.</summary>
        public IReadOnlyCollection<ObjectTypeDefinition> Types => _types.Values;

        /// <summary>The material with this id, or null.</summary>
        public MaterialDefinition? Material(string id) => _materials.GetValueOrDefault(id);

        /// <summary>The object type with this id, or null (a world database may name a type that was removed from the file).</summary>
        public ObjectTypeDefinition? Type(string? id) => id is not null && _types.TryGetValue(id, out var t) ? t : null;

        /// <summary>Weight in grams of one object of the type: the sum over its parts of density times volume; 0 for an unknown type.</summary>
        public int WeightGrams(string? typeId)
        {
            if (Type(typeId) is not { } type) return 0;
            var kilograms = type.Parts.Sum(p => _materials[p.MaterialId].DensityKgM3 * p.VolumeCm3 * 1e-6);
            return (int)Math.Round(kilograms * 1000.0);
        }

        /// <summary>The quality with which an object of the type serves as a tool of the class, or 0 when it does not.</summary>
        public double ToolQuality(string? typeId, string toolClass)
            => Type(typeId) is { } type && type.ToolClasses.TryGetValue(toolClass, out var q) ? q : 0.0;
    }
}
