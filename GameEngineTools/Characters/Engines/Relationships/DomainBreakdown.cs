// DomainBreakdown.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    /// <summary>
    /// Per-domain breakdown of <em>why</em> A values B.
    /// All values in [0, 100].
    /// </summary>
    public sealed record DomainBreakdown(
        double Intellect,
        double Humor,
        double Aesthetics,
        double Values,
        double Physical);
}
