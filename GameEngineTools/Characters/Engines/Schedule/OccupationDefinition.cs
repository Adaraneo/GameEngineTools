// OccupationDefinition.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using System;
    using System.Collections.Generic;

    // ─────────────────────────────────────────────────────────────────────────
    //  String constants for built-in occupation IDs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// String identifiers for the built-in occupation types registered by
    /// <see cref="BuiltInOccupationRegistrar"/>.
    /// Use these constants wherever an occupation ID is required to avoid
    /// magic strings and benefit from rename refactoring.
    /// </summary>
    public static class OccupationIds
    {
        /// <summary>No fixed occupation — character has no daily schedule.</summary>
        /// <remarks>
        /// In weights dictionaries use <c>""</c> (empty string) as the key for
        /// the "no occupation" probability.
        /// </remarks>
        public const string None = "";

        /// <summary>Craftsperson occupation.</summary>
        public const string Craftsperson = "craftsperson";

        /// <summary>Merchant occupation.</summary>
        public const string Merchant = "merchant";

        /// <summary>Scholar occupation.</summary>
        public const string Scholar = "scholar";

        /// <summary>Farmer occupation.</summary>
        public const string Farmer = "farmer";

        /// <summary>Guard occupation.</summary>
        public const string Guard = "guard";

        /// <summary>Healer occupation.</summary>
        public const string Healer = "healer";

        /// <summary>Artist occupation.</summary>
        public const string Artist = "artist";

        /// <summary>Laborer occupation.</summary>
        public const string Laborer = "laborer";

        /// <summary>All built-in non-empty occupation IDs.</summary>
        public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Craftsperson, Merchant, Scholar, Farmer, Guard, Healer, Artist, Laborer
        };

        /// <summary>
        /// Built-in occupations with high physical demand.
        /// These receive reduced weight for <c>MidAged</c> and <c>Old</c> characters.
        /// </summary>
        public static IReadOnlySet<string> PhysicalOccupations { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Guard, Laborer, Farmer };

        /// <summary>
        /// Built-in occupations that are sedentary / intellectual.
        /// Only these (plus <see cref="None"/>) are available for <c>Old</c> characters
        /// from the built-in set.
        /// </summary>
        public static IReadOnlySet<string> SedentaryOccupations { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Scholar, Healer, Artist };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Slot template
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Blueprint for a single time-anchored slot within an <see cref="OccupationDefinition"/>.
    /// <see cref="OccupationScheduleSeeder"/> converts templates into <see cref="ScheduleSlot"/>
    /// instances after applying personality modulation (chronotype shift, motivation boost).
    /// </summary>
    /// <param name="SlotId">
    /// Stable identifier used for scheduler cancellation and logging.
    /// Must be unique within the parent <see cref="OccupationDefinition"/>.
    /// </param>
    /// <param name="HourOfDay">Hour of day (0..HoursPerDay-1) at which this slot fires.</param>
    /// <param name="Action">
    /// Action name from <c>ActionNames</c> (e.g. <c>"Work"</c>, <c>"ReachOut"</c>).
    /// </param>
    /// <param name="LocationKey">
    /// Symbolic location key (e.g. <c>"WorkLocation"</c>). Resolved via
    /// <c>locationOverrides</c> in <see cref="OccupationScheduleSeeder.Seed"/>; stripped
    /// when no override is provided.
    /// </param>
    /// <param name="BiasStrength">Utility bias strength [0..1].</param>
    /// <param name="CanSkipWhenStressed">
    /// Whether the bias is suppressed when stress or energy thresholds are breached.
    /// </param>
    public sealed record ScheduleSlotTemplate(
        string SlotId,
        int HourOfDay,
        string Action,
        string? LocationKey,
        double BiasStrength,
        bool CanSkipWhenStressed);

    // ─────────────────────────────────────────────────────────────────────────
    //  Occupation definition
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Immutable definition of a character occupation — a named collection of
    /// time-anchored slot templates that form the character's daily routine.
    /// </summary>
    /// <param name="Id">
    /// Unique occupation identifier. Use lowercase kebab-case (e.g. <c>"craftsperson"</c>)
    /// for built-ins; prefix custom occupations with a namespace (e.g. <c>"mods.blacksmith"</c>).
    /// </param>
    /// <param name="Slots">Ordered list of slot templates. May be empty.</param>
    public sealed record OccupationDefinition(
        string Id,
        IReadOnlyList<ScheduleSlotTemplate> Slots);

    // ─────────────────────────────────────────────────────────────────────────
    //  Registry
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runtime registry of all known occupation definitions.
    /// Built-in occupations are pre-loaded by <see cref="BuiltInOccupationRegistrar"/>.
    /// Custom or mod occupations can be added at any time before character creation.
    /// </summary>
    public interface IOccupationRegistry
    {
        /// <summary>
        /// Registers a new occupation definition.
        /// </summary>
        /// <param name="definition">The definition to register.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if an occupation with the same <see cref="OccupationDefinition.Id"/>
        /// is already registered.
        /// </exception>
        void Register(OccupationDefinition definition);

        /// <summary>
        /// Returns the definition for the given ID, or <c>null</c> if not found.
        /// </summary>
        OccupationDefinition? TryGet(string id);

        /// <summary>All registered occupation IDs (excluding the empty/"none" sentinel).</summary>
        IEnumerable<string> AllIds { get; }
    }

    /// <summary>
    /// Default in-memory implementation of <see cref="IOccupationRegistry"/>.
    /// Thread-safe for concurrent reads after the initial registration phase.
    /// </summary>
    public sealed class DefaultOccupationRegistry : IOccupationRegistry
    {
        private readonly Dictionary<string, OccupationDefinition> _definitions =
            new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public void Register(OccupationDefinition definition)
        {
            if (definition is null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
                throw new ArgumentException("Occupation ID must not be empty.", nameof(definition));

            if (_definitions.ContainsKey(definition.Id))
                throw new InvalidOperationException(
                    $"Occupation '{definition.Id}' is already registered. Use a unique ID.");

            _definitions[definition.Id] = definition;
        }

        /// <inheritdoc/>
        public OccupationDefinition? TryGet(string id)
            => string.IsNullOrEmpty(id) ? null
               : _definitions.TryGetValue(id, out var def) ? def : null;

        /// <inheritdoc/>
        public IEnumerable<string> AllIds => _definitions.Keys;
    }
}
