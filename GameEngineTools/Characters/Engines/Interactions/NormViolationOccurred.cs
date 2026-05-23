// NormViolationOccurred.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Domain event emitted when a character commits a social action that violates
    /// an active norm on the interaction surface.
    /// </summary>
    /// <remarks>
    /// Consumed by:
    /// <list type="bullet">
    ///   <item><see cref="GameEngineTools.Characters.Engines.Psychology.DefaultPsychologyEngine"/> — applies shame spike to the actor (Sznycer 2016).</item>
    ///   <item>Future: <c>MemoryEngine</c> — encodes the episode with high salience.</item>
    ///   <item>Future: <c>AvoidanceLedger</c> — increments per-(ActionKind, SurfaceKind) penalty.</item>
    /// </list>
    /// </remarks>
    /// <param name="OccurredAt">Simulation timestamp.</param>
    /// <param name="Actor">The character who committed the norm-violating action.</param>
    /// <param name="NormKind">The type of norm that was violated.</param>
    /// <param name="ViolationScore">
    /// Computed product of <c>Severity × EnforcementProbability × P(audience)</c> [0..1].
    /// Used by <c>DefaultPsychologyEngine</c> to scale the shame spike magnitude.
    /// </param>
    /// <param name="HasAudience">
    /// True when <c>InteractionSurface.Observers</c> is non-empty.
    /// Amplifies arousal component of the shame spike (Sznycer 2016: social evaluation context).
    /// </param>
    public sealed record NormViolationOccurred(
        WDateTime OccurredAt,
        HumanId Actor,
        SocialNormKind NormKind,
        double ViolationScore,
        bool HasAudience) : IDomainEvent;
}
