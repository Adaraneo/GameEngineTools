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

    /// <summary>What kind of task it is.</summary>
    public enum TaskKind
    {
        /// <summary>Turns inputs into one product at a place of a location type (a recipe).</summary>
        Craft,

        /// <summary>A job on a field; the name of the action it is carried out as, and the tool it needs.</summary>
        FieldWork,
    }

    /// <summary>An item and a count, as a task consumes or produces it. <paramref name="Item"/> names an item kind (today the <c>PickupItemKind</c> names; later object type ids).</summary>
    public sealed record TaskItem(string Item, int Quantity);

    /// <summary>One thing that can be done. A <see cref="TaskKind.Craft"/> task has inputs, one output, a duration and a location type; a <see cref="TaskKind.FieldWork"/> task names its action and the tool class it needs.</summary>
    /// <param name="Id">Stable id.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Kind">Craft or field work.</param>
    /// <param name="Inputs">What a craft task consumes.</param>
    /// <param name="Output">What a craft task makes.</param>
    /// <param name="OutputName">Display name of the product.</param>
    /// <param name="DurationMinutes">Minutes a craft task takes.</param>
    /// <param name="Location">Location type a craft task must be done at (mill, bakery, dairy).</param>
    /// <param name="Action">The action a field task is carried out as (Plough, Sow, Reap, Thresh).</param>
    /// <param name="ToolClass">The tool class the task needs, or null when it needs none.</param>
    /// <param name="Source">Where the numbers come from.</param>
    /// <param name="Requires">The conditions of a field task, all of which must hold (<see cref="GameEngineTools.World.Objects.Crops.FieldConditions"/>).</param>
    public sealed record TaskDefinition(
        string Id,
        string Name,
        TaskKind Kind,
        IReadOnlyList<TaskItem> Inputs,
        TaskItem? Output,
        string? OutputName,
        int DurationMinutes,
        string? Location,
        string? Action,
        string? ToolClass,
        string? Source = null,
        IReadOnlyList<string>? Requires = null)
    {
        /// <summary>The conditions of a field task, never null.</summary>
        public IReadOnlyList<string> Requires { get; init; } = Requires ?? Array.Empty<string>();
    }

    /// <summary>The materials and object types the work system knows; the world database stores only a type id and the state of an instance, this holds what the type is.</summary>
    public sealed class WorkCatalog
    {
        private readonly Dictionary<string, MaterialDefinition> _materials;
        private readonly Dictionary<string, ObjectTypeDefinition> _types;
        private readonly Dictionary<string, TaskDefinition> _tasksById = new(StringComparer.Ordinal);
        private readonly List<TaskDefinition> _tasks = new();

        /// <summary>Builds and checks a catalog.</summary>
        /// <exception cref="FormatException">An id is duplicated or empty, a density or volume is not positive, a part names an unknown material, or a task is incomplete.</exception>
        public WorkCatalog(IEnumerable<MaterialDefinition> materials, IEnumerable<ObjectTypeDefinition> types, IEnumerable<TaskDefinition>? tasks = null)
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

            foreach (var task in tasks ?? Array.Empty<TaskDefinition>())
            {
                if (string.IsNullOrWhiteSpace(task.Id)) throw new FormatException("A task has no id.");
                if (task.Kind == TaskKind.Craft)
                {
                    if (task.Output is null || task.Output.Quantity <= 0) throw new FormatException($"Task '{task.Id}': a craft task needs an output with a positive quantity.");
                    if (task.Inputs.Count == 0 || task.Inputs.Any(i => i.Quantity <= 0)) throw new FormatException($"Task '{task.Id}': a craft task needs inputs with positive quantities.");
                    if (task.DurationMinutes <= 0) throw new FormatException($"Task '{task.Id}': a craft task needs a positive duration.");
                    if (string.IsNullOrWhiteSpace(task.Location)) throw new FormatException($"Task '{task.Id}': a craft task needs a location type.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(task.Action) || !Enum.GetNames<GameEngineTools.World.Objects.Crops.FieldTask>().Contains(task.Action, StringComparer.Ordinal))
                        throw new FormatException($"Task '{task.Id}': a field task needs the action it is carried out as ({string.Join(", ", Enum.GetNames<GameEngineTools.World.Objects.Crops.FieldTask>())}), not '{task.Action}'.");
                    if (task.Requires.Count == 0) throw new FormatException($"Task '{task.Id}': a field task needs at least one condition in 'requires'.");
                    foreach (var condition in task.Requires)
                        if (!GameEngineTools.World.Objects.Crops.FieldConditions.IsKnown(condition))
                            throw new FormatException($"Task '{task.Id}': unknown condition '{condition}'.");
                }

                if (!_tasksById.TryAdd(task.Id, task)) throw new FormatException($"Duplicate task '{task.Id}'.");
                _tasks.Add(task);
            }
        }

        /// <summary>All tasks, in the order of the file; where several could apply, the first decides.</summary>
        public IReadOnlyList<TaskDefinition> Tasks => _tasks;

        /// <summary>The task with this id, or null.</summary>
        public TaskDefinition? Task(string id) => _tasksById.GetValueOrDefault(id);

        /// <summary>The field task carried out as the given action (Plough, Sow, Reap, Thresh), or null.</summary>
        public TaskDefinition? FieldTaskFor(string action)
            => _tasks.FirstOrDefault(t => t.Kind == TaskKind.FieldWork && string.Equals(t.Action, action, StringComparison.Ordinal));

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
