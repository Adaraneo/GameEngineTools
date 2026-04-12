// FirstImpressionFormed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>Fired when character A meets B for the first time and forms an initial impression.</summary>
    /// <param name="Like">Initial like score derived from the halo effect.</param>
    /// <param name="Attraction">Overall attraction score in [0, 100].</param>
    /// <param name="BasePhysical">
    /// Evolutionary baseline component from <c>DefaultAttractionCalculator</c> — WHR, height, symmetry.
    /// Range: [0, 40]. Used to seed the <c>Physical</c> domain in <see cref="DomainBreakdown"/>.
    /// </param>
    /// <param name="PreferenceMatch">
    /// Personal preference match component from <c>DefaultAttractionCalculator</c>.
    /// Range: [0, 35]. Used to seed the <c>Aesthetics</c> domain in <see cref="DomainBreakdown"/>.
    /// </param>
    public sealed record FirstImpressionFormed(
        WDateTime OccurredAt,
        HumanId A,
        HumanId B,
        double Like,
        double Attraction,
        double BasePhysical = 0.0,
        double PreferenceMatch = 0.0,
        SexBiology? ABiology = null,
        SexBiology? BBiology = null) : IDomainEvent;
}
