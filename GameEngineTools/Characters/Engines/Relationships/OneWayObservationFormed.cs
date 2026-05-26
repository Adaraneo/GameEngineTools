// OneWayObservationFormed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Fired when a character observes another without being perceived in return.
    /// Seeds a one-sided, weaker relationship edge only on the observer's side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Models parasocial observation, unrequited noticing, and asymmetric pre-contact
    /// attraction. Real-world cases: repeatedly seeing the same person in a location
    /// without mutual acknowledgment; knowing someone by appearance before ever speaking.
    /// </para>
    /// <para>
    /// Mere exposure effect operates asymmetrically (Zajonc 1968) — familiarity builds
    /// on the perceiver's side regardless of reciprocity. No halo effect is applied
    /// because no social validation has occurred yet.
    /// </para>
    /// <para>
    /// When a mutual <see cref="FirstImpressionFormed"/> eventually occurs, the
    /// observer's one-sided edge is blended via Lerp — prior history is preserved,
    /// not overwritten.
    /// </para>
    /// </remarks>
    /// <param name="Observer">The character who notices the target.</param>
    /// <param name="Target">The character who is unaware of being observed.</param>
    /// <param name="Like">Observer's initial like score [0, 100].</param>
    /// <param name="Attraction">Observer's overall attraction to the target [0, 100].</param>
    /// <param name="TargetBiology">Biology of the target — for orientation-aware downstream scoring.</param>
    /// <param name="BasePhysical">Raw physical component [0, 40] from AttractionCalculator.</param>
    /// <param name="PreferenceMatch">Preference match component [0, 35] from AttractionCalculator.</param>
    public sealed record OneWayObservationFormed(
        WDateTime OccurredAt,
        HumanId Observer,
        HumanId Target,
        double Like,
        double Attraction,
        SexBiology? TargetBiology = null,
        double BasePhysical = 0.0,
        double PreferenceMatch = 0.0) : IDomainEvent;
}
