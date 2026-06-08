// CommunityReputationLedger.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Reputation
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Scene-level aggregate that distils per-observer <see cref="ThirdPartyActionObserved"/> events
    /// into a location-scoped <see cref="CommunityReputation"/> per subject.
    /// </summary>
    /// <remarks>
    /// The per-observer relationship edges (handled by the relationships engine) are unchanged; this
    /// ledger sits beside them and answers community-level questions — most importantly, the
    /// initial-trust prior a newcomer to a locale receives before any personal history exists.
    /// </remarks>
    public sealed class CommunityReputationLedger
    {
        private readonly Dictionary<(HumanId Subject, string Location), CommunityReputation> _map = new();
        private readonly double _halfLife;

        /// <summary>Creates a ledger with the given reputation decay half-life.</summary>
        /// <param name="halfLifeInteractions">Number of interactions over which reputation weight halves.</param>
        public CommunityReputationLedger(double halfLifeInteractions = ReputationMath.DefaultHalfLifeInteractions)
            => _halfLife = halfLifeInteractions;

        /// <summary>Folds one observed act about <paramref name="subject"/> at a locale into the aggregate.</summary>
        public void Observe(HumanId subject, string locationId, ThirdPartyObservationType type, WDateTime now)
        {
            var key = (subject, locationId);
            var current = _map.TryGetValue(key, out var existing)
                ? existing
                : new CommunityReputation(subject, locationId, 0.0, 0.0, now);

            _map[key] = current with
            {
                Score = ReputationMath.UpdateScore(current.Score, type, _halfLife),
                Spread = ReputationMath.UpdateSpread(current.Spread),
                LastUpdatedAt = now
            };
        }

        /// <summary>Convenience overload that folds a domain event at a given locale.</summary>
        public void Observe(ThirdPartyActionObserved observation, string locationId)
            => Observe(observation.Actor, locationId, observation.Type, observation.OccurredAt);

        /// <summary>Returns the aggregate reputation, or <c>null</c> when the subject is unknown at the locale.</summary>
        public CommunityReputation? Get(HumanId subject, string locationId)
            => _map.TryGetValue((subject, locationId), out var r) ? r : null;

        /// <summary>
        /// The initial trust a newcomer to <paramref name="locationId"/> receives toward
        /// <paramref name="subject"/>, derived from the community's aggregate reputation
        /// (or the neutral baseline when none exists).
        /// </summary>
        public double InitialTrustPrior(HumanId subject, string locationId)
        {
            var r = Get(subject, locationId);
            return r is null
                ? ReputationMath.DefaultTrustPrior
                : ReputationMath.InitialTrustPrior(r.Score, r.Spread);
        }
    }
}
