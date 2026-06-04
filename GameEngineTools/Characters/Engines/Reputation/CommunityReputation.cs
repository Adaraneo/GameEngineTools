// CommunityReputation.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Reputation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// A location-scoped aggregate of how a community perceives one subject — distinct from the
    /// per-observer relationship edges that feed it.
    /// </summary>
    /// <param name="Subject">The character the reputation is about.</param>
    /// <param name="LocationId">The locale the reputation is held in (reputations are local).</param>
    /// <param name="Score">Image score [-1..+1]: + good standing, − bad standing.</param>
    /// <param name="Spread">
    /// Fraction of the community that holds this reputation [0..1] — the <c>q</c> in the
    /// Nowak–Sigmund cooperation-stability condition <c>q &gt; c/b</c>.
    /// </param>
    /// <param name="LastUpdatedAt">Timestamp of the most recent contributing observation.</param>
    public sealed record CommunityReputation(
        HumanId Subject,
        string LocationId,
        double Score,
        double Spread,
        WDateTime LastUpdatedAt);
}
