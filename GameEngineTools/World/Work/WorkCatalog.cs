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
    /// <param name="FixedWeightGrams">Weight in grams for a type without parts (food, grain); a type with parts weighs what its parts weigh.</param>
    /// <param name="Affordances">What using an instance gives, as affordance name to satisfaction (Hunger, Thirst, Ownership ...).</param>
    /// <param name="SpoilageHours">Hours until a fresh instance has spoiled completely; null when it does not spoil.</param>
    /// <param name="ReferencePrice">Starting price and barter value; null means the catalog default.</param>
    /// <param name="Czech">How the type is named in Czech for dialogue, or null when it is never spoken about.</param>
    public sealed record ObjectTypeDefinition(
        string Id,
        string Name,
        WorldObjectCategory Category,
        bool Pickable,
        IReadOnlyList<ObjectPart> Parts,
        IReadOnlyDictionary<string, double> ToolClasses,
        double? DurabilityHours = null,
        string? Source = null,
        int? FixedWeightGrams = null,
        IReadOnlyDictionary<string, double>? Affordances = null,
        double? SpoilageHours = null,
        double? ReferencePrice = null,
        CzechNoun? Czech = null);

    /// <summary>What kind of task it is.</summary>
    public enum TaskKind
    {
        /// <summary>Turns inputs into one product at a place of a location type (a recipe).</summary>
        Craft,

        /// <summary>A job on a field; the name of the action it is carried out as, and the tool it needs.</summary>
        FieldWork,
    }

    /// <summary>An item and a count, as a task consumes or produces it. <paramref name="Item"/> is the id of an object type of the catalog.</summary>
    public sealed record TaskItem(string Item, int Quantity);

    /// <summary>One thing that can be done. A <see cref="TaskKind.Craft"/> task has inputs, one output, a duration and a location type; a <see cref="TaskKind.FieldWork"/> task names its action and the tool class it needs.</summary>
    /// <param name="Id">Stable id.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Kind">Craft or field work.</param>
    /// <param name="Inputs">What a craft task consumes.</param>
    /// <param name="Output">What a craft task makes.</param>
    /// <param name="OutputName">Display name of the product.</param>
    /// <param name="DurationMinutes">Minutes a craft task takes.</param>
    /// <param name="Station">Id of the object type a craft task is done at (mill, oven, dairy): the workstation, which may serve several tasks.</param>
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
        string? Station,
        string? Action,
        string? ToolClass,
        string? Source = null,
        IReadOnlyList<string>? Requires = null)
    {
        /// <summary>The conditions of a field task, never null.</summary>
        public IReadOnlyList<string> Requires { get; init; } = Requires ?? Array.Empty<string>();
    }

    /// <summary>An occupation as the work system sees it: the tasks of <c>tasks.json</c> its holder does. The daily rhythm (hours, places) stays in the schedule slots of the occupation.</summary>
    /// <param name="Id">Occupation id, as in the schedule (compared ignoring case).</param>
    /// <param name="TaskIds">Ids of the tasks it does.</param>
    public sealed record OccupationWork(string Id, IReadOnlyList<string> TaskIds);

    /// <summary>The materials and object types the work system knows; the world database stores only a type id and the state of an instance, this holds what the type is.</summary>
    public sealed class WorkCatalog
    {
        private readonly Dictionary<string, MaterialDefinition> _materials;
        private readonly Dictionary<string, ObjectTypeDefinition> _types;
        private readonly Dictionary<string, TaskDefinition> _tasksById = new(StringComparer.Ordinal);
        private readonly List<TaskDefinition> _tasks = new();
        private readonly Dictionary<string, List<TaskDefinition>> _stationTasks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, OccupationWork> _occupations = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Builds and checks a catalog.</summary>
        /// <exception cref="FormatException">An id is duplicated or empty, a density or volume is not positive, a part names an unknown material, or a task is incomplete.</exception>
        public WorkCatalog(IEnumerable<MaterialDefinition> materials, IEnumerable<ObjectTypeDefinition> types, IEnumerable<TaskDefinition>? tasks = null, IEnumerable<OccupationWork>? occupations = null)
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

                if (t.Parts.Count == 0 && t.FixedWeightGrams is not > 0)
                    throw new FormatException($"Object type '{t.Id}': it needs parts or a positive weightGrams.");
                foreach (var affordance in t.Affordances?.Keys ?? Enumerable.Empty<string>())
                    if (!Enum.TryParse<AffordanceType>(affordance, ignoreCase: false, out _))
                        throw new FormatException($"Object type '{t.Id}': unknown affordance '{affordance}'.");
                if (t.SpoilageHours is { } sh && !(sh > 0.0)) throw new FormatException($"Object type '{t.Id}': spoilageHours must be positive.");
                if (t.ReferencePrice is { } rp && !(rp >= 0.0)) throw new FormatException($"Object type '{t.Id}': referencePrice must not be negative.");
                foreach (var (toolClass, quality) in t.ToolClasses)
                    if (!(quality > 0.0)) throw new FormatException($"Object type '{t.Id}': tool class '{toolClass}' needs a positive quality.");
                if (t.DurabilityHours is { } d && !(d > 0.0)) throw new FormatException($"Object type '{t.Id}': durability must be positive.");
                if (t.Czech is { } czech && (string.IsNullOrWhiteSpace(czech.Lemma) || string.IsNullOrWhiteSpace(czech.Pattern) || !CzechNoun.Genders.Contains(czech.Gender)))
                {
                    throw new FormatException($"Object type '{t.Id}': czech needs a lemma, a pattern and a gender ({string.Join(", ", CzechNoun.Genders)}).");
                }

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
                    if (string.IsNullOrWhiteSpace(task.Station)) throw new FormatException($"Task '{task.Id}': a craft task needs the workstation it is done at ('station').");
                    if (!_types.ContainsKey(task.Station)) throw new FormatException($"Task '{task.Id}': the workstation '{task.Station}' is not an object type of objecttypes.json.");
                    if (!_stationTasks.TryGetValue(task.Station, out var atStation)) _stationTasks[task.Station] = atStation = new List<TaskDefinition>();
                    atStation.Add(task);
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

            foreach (var occupation in occupations ?? Array.Empty<OccupationWork>())
            {
                if (string.IsNullOrWhiteSpace(occupation.Id)) throw new FormatException("An occupation has no id.");
                if (occupation.TaskIds.Count == 0) throw new FormatException($"Occupation '{occupation.Id}': it lists no task.");
                foreach (var taskId in occupation.TaskIds)
                    if (!_tasksById.ContainsKey(taskId))
                        throw new FormatException($"Occupation '{occupation.Id}': '{taskId}' is not a task of tasks.json.");
                if (occupation.TaskIds.Distinct(StringComparer.Ordinal).Count() != occupation.TaskIds.Count)
                    throw new FormatException($"Occupation '{occupation.Id}': a task is listed twice.");
                if (!_occupations.TryAdd(occupation.Id, occupation)) throw new FormatException($"Duplicate occupation '{occupation.Id}'.");
            }
        }

        /// <summary>The work of an occupation, or null for an occupation that lists none (a scholar, a guard: only their schedule).</summary>
        public OccupationWork? Occupation(string? id) => string.IsNullOrEmpty(id) ? null : _occupations.GetValueOrDefault(id);

        /// <summary>All occupations that list tasks.</summary>
        public IReadOnlyCollection<OccupationWork> Occupations => _occupations.Values;

        /// <summary>The tool classes the field tasks of the occupation need (distinct), empty for an occupation that lists none.</summary>
        public IReadOnlyList<string> ToolClassesOfOccupation(string? occupationId)
        {
            return Occupation(occupationId)?.TaskIds
                .Select(id => _tasksById[id])
                .Where(task => task is { Kind: TaskKind.FieldWork, ToolClass: not null })
                .Select(task => task.ToolClass!)
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>Whether the occupation does the task with this id.</summary>
        public bool OccupationDoes(string? occupationId, string taskId) => Occupation(occupationId)?.TaskIds.Contains(taskId) == true;

        /// <summary>Whether the occupation does any task of this kind (a farmer: field work; a miller: craft).</summary>
        public bool OccupationDoesKind(string? occupationId, TaskKind kind)
            => Occupation(occupationId)?.TaskIds.Any(id => _tasksById[id].Kind == kind) == true;

        /// <summary>Whether the occupation does a field task carried out as this action (Plough, Sow, Reap, Thresh).</summary>
        public bool OccupationDoesAction(string? occupationId, string action)
            => Occupation(occupationId)?.TaskIds.Any(id => _tasksById[id] is { Kind: TaskKind.FieldWork } t && string.Equals(t.Action, action, StringComparison.Ordinal)) == true;

        /// <summary>All tasks, in the order of the file; where several could apply, the first decides.</summary>
        public IReadOnlyList<TaskDefinition> Tasks => _tasks;

        /// <summary>The task with this id, or null.</summary>
        public TaskDefinition? Task(string id) => _tasksById.GetValueOrDefault(id);

        /// <summary>Whether an object type is a workstation: some craft task is done at it.</summary>
        public bool IsStation(string? typeId) => typeId is not null && _stationTasks.ContainsKey(typeId);

        /// <summary>The craft tasks done at a workstation type, in the order of the file; empty for a type that is not a workstation.</summary>
        public IReadOnlyList<TaskDefinition> TasksAt(string? typeId)
            => typeId is not null && _stationTasks.TryGetValue(typeId, out var list) ? list : Array.Empty<TaskDefinition>();

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

        /// <summary>Weight in grams of one object of the type: the sum over its parts of density times volume, or its fixed weight when it has no parts.</summary>
        /// <exception cref="KeyNotFoundException">The catalog has no such type.</exception>
        public int WeightGrams(string? typeId)
        {
            var type = Type(typeId) ?? throw new KeyNotFoundException($"The work catalog has no object type '{typeId}'.");
            if (type.Parts.Count == 0)
                return type.FixedWeightGrams!.Value;
            var kilograms = type.Parts.Sum(p => _materials[p.MaterialId].DensityKgM3 * p.VolumeCm3 * 1e-6);
            return (int)Math.Round(kilograms * 1000.0);
        }

        /// <summary>Price used where nothing else is known: a type without a reference price, or an object without a type.</summary>
        public const double DefaultReferencePrice = 1.0;

        /// <summary>Starting price and barter value of an item type; <see cref="DefaultReferencePrice"/> for an untyped object or a type that names none.</summary>
        public double ReferencePrice(string? typeId) => Type(typeId)?.ReferencePrice ?? DefaultReferencePrice;

        /// <summary>Share of freshness an instance of the type loses per hour; 0 when it does not spoil.</summary>
        public double SpoilagePerHour(string? typeId) => Type(typeId)?.SpoilageHours is { } hours ? 1.0 / hours : 0.0;

        /// <summary>The quality with which an object of the type serves as a tool of the class, or 0 when it does not.</summary>
        public double ToolQuality(string? typeId, string toolClass)
            => Type(typeId) is { } type && type.ToolClasses.TryGetValue(toolClass, out var q) ? q : 0.0;
    }
}
