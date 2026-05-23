// CharacterData.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Persistence
{
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Serialisation DTO for a single character — used by <see cref="GameEngineTools.FileSystem.GeneratedFile"/>
    /// to persist and restore full character state between sessions.
    /// </summary>
    public sealed record CharacterData
    {
        /// <summary>Gets the unique identifier of the character.</summary>
        public HumanId Id { get; init; }

        /// <summary>Gets the character's identity (name, birth date).</summary>
        public required Identity Identity { get; init; }

        /// <summary>Gets the biological sex of the character.</summary>
        public SexBiology Biology { get; init; }

        /// <summary>Gets the personality traits of the character.</summary>
        public required Personality Personality { get; init; }

        /// <summary>Gets the immutable genetic blueprint used to project physical appearance at runtime.</summary>
        public required GeneticBlueprint GeneticBlueprint { get; init; }

        /// <summary>
        /// Gets the character's personal attraction preferences (generated once at creation).
        /// </summary>
        /// <remarks>
        /// Nullable for backwards compatibility with saves that pre-date this field.
        /// <see cref="GameEngineTools.FileSystem.GeneratedFile"/> falls back to a default profile when null.
        /// </remarks>
        public AttractionProfile? AttractionProfile { get; init; }

        /// <summary>Gets the last known engine snapshot (physiology, psychology, behavior, …).</summary>
        public required EnginesSnapshot Snapshot { get; init; }

        /// <summary>Gets the maximum health points of the character.</summary>
        public double MaxHealth { get; init; }

        /// <summary>Gets the current health points of the character.</summary>
        public double Health { get; init; }

        /// <summary>Gets the equipped armour set, or <c>null</c> if none.</summary>
        public ArmorSet? Armor { get; init; }

        /// <summary>Gets the equipped weapon, or <c>null</c> if none.</summary>
        public Weapon? Weapon { get; init; }

        /// <summary>Gets the total protection value derived from armour and other sources.</summary>
        public double Protection { get; init; }

        /// <summary>
        /// Gets the character's occupation ID (see <c>OccupationIds</c>), used to re-seed the
        /// daily schedule on import. <c>null</c> means no fixed occupation.
        /// </summary>
        public string? Occupation { get; init; }
    }
}
