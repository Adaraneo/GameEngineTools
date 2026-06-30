// BereavementEvents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Bereavement
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Delivered to a mourner when someone they were bonded to dies. The bereavement engine creates a
    /// <see cref="LossRecord"/>, and Psychology applies the acute grief spike (Sadness, MoodBaseline↓, Stress↑).
    /// </summary>
    /// <param name="OccurredAt">When the loss is registered.</param>
    /// <param name="Human">The mourner.</param>
    /// <param name="Deceased">The person who died.</param>
    /// <param name="BondStrength">Strength of the lost bond (0..100), driving grief intensity.</param>
    /// <param name="Cause">Cause of death — a violent/sudden cause sharply raises prolonged-grief risk.</param>
    /// <param name="KinRole">The mourner's kin relationship to the deceased.</param>
    public sealed record BereavementOnset(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Deceased,
        double BondStrength,
        DeathCause Cause,
        KinRole KinRole) : IDomainEvent;

    /// <summary>Emitted when a grief trajectory class is assigned to a fresh loss (observability).</summary>
    public sealed record GriefTrajectoryAssigned(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Deceased,
        GriefTrajectory Trajectory) : IDomainEvent;

    /// <summary>
    /// A "wave of grief": emitted when a loss re-enters the loss-orientation phase of the DPM oscillation.
    /// Carries the affect deltas Psychology applies (consumed in the same tick via self-delivery).
    /// </summary>
    /// <param name="OccurredAt">When the pang occurs.</param>
    /// <param name="Human">The mourner.</param>
    /// <param name="Deceased">The person being grieved.</param>
    /// <param name="Intensity">Current grief intensity (0..100) driving the pang magnitude.</param>
    /// <param name="ValenceDelta">Immediate PAD valence drop [−1..0].</param>
    /// <param name="MoodBaselineDelta">Persistent mood-baseline drop [0..100 scale, negative].</param>
    /// <param name="StressDelta">Stress spike [0..100 scale].</param>
    public sealed record GriefPang(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Deceased,
        double Intensity,
        double ValenceDelta,
        double MoodBaselineDelta,
        double StressDelta) : IDomainEvent;

    /// <summary>
    /// A funeral / mourning gathering for the deceased — regained control &amp; closure reduce grief
    /// (Norton &amp; Gino 2014 <i>JEP:General</i> 143:266) and the co-located gathering boosts cohesion.
    /// </summary>
    /// <param name="OccurredAt">When the funeral is held.</param>
    /// <param name="Human">The mourner attending.</param>
    /// <param name="Deceased">The deceased being mourned.</param>
    /// <param name="Attendees">Number of co-located mourners (community cohesion signal).</param>
    public sealed record FuneralHeld(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Deceased,
        int Attendees) : IDomainEvent;

    /// <summary>Emitted when a deceased is physically buried (Stage 2 world burial). Reserved.</summary>
    public sealed record Buried(WDateTime OccurredAt, HumanId Human, HumanId Deceased) : IDomainEvent;

    /// <summary>Emitted when a mourner visits the grave of the deceased (Stage 2). Reserved.</summary>
    public sealed record GraveVisited(WDateTime OccurredAt, HumanId Human, HumanId Deceased) : IDomainEvent;

    /// <summary>
    /// Emitted when a surviving partner's widowhood mortality-hazard multiplier changes — for
    /// observability of the "widowhood effect" (Moon 2011; Shor 2012; Parkes 1969).
    /// </summary>
    /// <param name="OccurredAt">When the change occurs.</param>
    /// <param name="Human">The surviving partner.</param>
    /// <param name="HazardMultiplier">The mortality-risk multiplier now in effect (≥1).</param>
    public sealed record WidowhoodHazardChanged(
        WDateTime OccurredAt,
        HumanId Human,
        double HazardMultiplier) : IDomainEvent;
}
