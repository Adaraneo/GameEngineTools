// WorldObject.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System.Collections.Immutable;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Represents a physical or conceptual object present in a world location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NPCs perceive world objects as structured semantic data — not as visual pixels.
    /// This mirrors how human cognition works: we perceive "chair" and "affordance: sit",
    /// not raw photon data.
    /// </para>
    /// <para>
    /// <b>Unity integration note (Phase 2):</b><br/>
    /// When connected to Unity, <see cref="WorldObject"/> instances will be constructed
    /// from <c>GameObject</c> metadata via <c>UnityWorldObjectProvider</c>.
    /// The simulation engine never changes — only the provider implementation does.
    /// </para>
    /// </remarks>
    public sealed record WorldObject
    {
        #region Identity

        /// <summary>
        /// Unique identifier within the scene (e.g. "fireplace_01", "chair_north").
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Human-readable label used in narrative output and debug tools.
        /// </summary>
        public required string DisplayName { get; init; }

        /// <summary>
        /// Semantic category that drives high-level NPC behavior classification.
        /// </summary>
        public required WorldObjectCategory Category { get; init; }

        /// <summary>
        /// ID of the location this object belongs to.
        /// Must match a registered <see cref="LocationDescriptor.Id"/>.
        /// </summary>
        public required string LocationId { get; init; }

        #endregion

        #region Perceptual signature

        /// <summary>
        /// Perceived temperature contribution from this object. Range [0, 1].
        /// 0 = cold or neutral, 1 = intensely hot.
        /// Contributes to ambient stress and comfort modifiers.
        /// </summary>
        public double HeatSignature { get; init; } = 0.0;

        /// <summary>
        /// Noise emitted by this object. Range [0, 1].
        /// Added on top of <see cref="LocationDescriptor.BaseNoise"/> when computing
        /// the effective noise level of a location.
        /// </summary>
        public double AmbientNoise { get; init; } = 0.0;

        /// <summary>
        /// Whether this object physically blocks passage or line of sight.
        /// Reserved for Phase 2 spatial reasoning; ignored in Phase 1.
        /// </summary>
        public bool BlocksLineOfSight { get; init; } = false;

        #endregion

        #region Behavioral affordances

        /// <summary>
        /// The set of behavioral affordances this object provides to an NPC.
        /// </summary>
        /// <seealso cref="WorldObjectAffordance"/>
        public ImmutableArray<WorldObjectAffordance> Affordances { get; init; }
            = ImmutableArray<WorldObjectAffordance>.Empty;

        /// <summary>
        /// Whether this object is currently available for NPC interaction.
        /// False if broken, occupied, or otherwise inaccessible.
        /// </summary>
        public bool IsAvailable { get; init; } = true;

        #endregion

        #region Pickup properties

        /// <summary>
        /// Whether this object can be picked up and added to an NPC's inventory.
        /// </summary>
        public bool IsPickable { get; init; } = false;

        /// <summary>
        /// Weight of this object in grams. Used to compute inventory carry weight.
        /// </summary>
        public int WeightGrams { get; init; } = 0;

        /// <summary>
        /// Semantic category used by the inventory engine when resolving pickup behaviour.
        /// </summary>
        public PickupItemKind ItemKind { get; init; } = PickupItemKind.None;

        #endregion

        #region Runtime ownership state

        /// <summary>
        /// Character currently holding this object, or <c>null</c> if it is not held.
        /// Set by <see cref="CsvWorldObjectProvider.SetHeldBy"/> when a character picks up the object.
        /// </summary>
        public HumanId? HeldBy { get; init; } = null;

        /// <summary>
        /// When the object was consumed (eaten, drunk, destroyed), or <c>null</c> if still available.
        /// Used by <see cref="ObjectRespawnScheduler"/> to compute respawn eligibility.
        /// </summary>
        public WDateTime? ConsumedAt { get; init; } = null;

        /// <summary>
        /// Whether this object respawns after being consumed.
        /// </summary>
        public bool Respawns { get; init; } = false;

        /// <summary>
        /// How many in-world minutes after consumption the object reappears.
        /// Only meaningful when <see cref="Respawns"/> is <c>true</c>.
        /// </summary>
        public int RespawnMinutes { get; init; } = 1440;

        #endregion
    }
}
