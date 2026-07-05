// WorldObjectAffordance.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    /// <summary>
    /// Represents a single behavioral affordance that a <see cref="WorldObject"/>
    /// can satisfy for an NPC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An affordance answers the question: <em>"What can I do with/near this object,
    /// and how much does it help me?"</em>
    /// </para>
    /// <para>
    /// Example — a fireplace:
    /// <code>
    /// new WorldObjectAffordance(AffordanceType.Warmth,       satisfaction: 0.8),
    /// new WorldObjectAffordance(AffordanceType.Social,       satisfaction: 0.3),
    /// new WorldObjectAffordance(AffordanceType.MoodBoost,    satisfaction: 0.2)
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="Type">The type of need or drive this affordance addresses.</param>
    /// <param name="Satisfaction">
    /// How strongly this object satisfies the need. Range [0, 1].
    /// 1.0 = full satisfaction; 0.0 = no effect.
    /// </param>
    public sealed record WorldObjectAffordance(
        AffordanceType Type,
        double Satisfaction);

    /// <summary>
    /// Type of behavioral need that a world object can address.
    /// Maps onto the NPC need system in <c>BehaviorEngine</c>.
    /// </summary>
    public enum AffordanceType
    {
        /// <summary>Addresses hunger — food.</summary>
        Hunger,

        /// <summary>Addresses thirst — water, ale, wine.</summary>
        Thirst,

        /// <summary>Addresses fatigue — bed, chair, bench.</summary>
        Rest,

        /// <summary>Addresses need for social stimulation — communal spaces, fireplaces.</summary>
        Social,

        /// <summary>Addresses need for productive activity — tools, workbenches.</summary>
        Work,

        /// <summary>Addresses entertainment or fun needs — lute, game board.</summary>
        Entertainment,

        /// <summary>Provides warmth — reduces cold-related stress.</summary>
        Warmth,

        /// <summary>Boosts mood / valence directly — art, pleasant smell.</summary>
        MoodBoost,

        /// <summary>
        /// Raises stress or threat perception — hazards, weapons, fire.
        /// Negative affordance: satisfaction here means "how much stress it adds".
        /// </summary>
        StressRaise,

        /// <summary>
        /// Signals that an object can be taken and carried by the character.
        /// Satisfaction = desirability of taking this object [0, 1].
        /// </summary>
        Ownership,

        /// <summary>
        /// Marks a location/tool object as capable of producing or processing food via labor
        /// (see <c>ProductionService</c>). What it produces is the carrying object's
        /// <see cref="PickupItemKind"/> (raw production) or the matching <c>Recipe</c> output
        /// (processing). Satisfaction is unused (0). Food-economy Tier 1.
        /// </summary>
        Production
    }
}
