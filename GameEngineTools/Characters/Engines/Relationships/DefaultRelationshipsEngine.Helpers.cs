// DefaultRelationshipsEngine.Helpers.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Logging;

    internal sealed partial class DefaultRelationshipsEngine
    {
        #region Private methods — DomainBreakdown

        /// <summary>
        /// Updates <see cref="DomainBreakdown"/> based on the type of speech act.
        /// </summary>
        /// <remarks>
        /// Each <see cref="SpeechAct"/> affects different domains.
        /// Rejected interactions have half the effect because the signal was registered but not welcomed.
        /// </remarks>
        private static DomainBreakdown ApplyDomainBoost(DomainBreakdown bd, SpeechAct act, bool accepted)
        {
            var mul = accepted ? 1.0 : 0.5;

            return act switch
            {
                SpeechAct.SmallTalk => bd with { Humor = BumpD(bd.Humor, +1.5 * mul) },
                SpeechAct.Question => bd with { Intellect = BumpD(bd.Intellect, +2.0 * mul) },
                SpeechAct.SelfDisclosure => bd with { Values = BumpD(bd.Values, +2.0 * mul) },
                SpeechAct.Validation => bd with { Values = BumpD(bd.Values, +1.0 * mul) },
                SpeechAct.Humor => bd with { Humor = BumpD(bd.Humor, +2.5 * mul) },
                SpeechAct.Meta => bd with { Intellect = BumpD(bd.Intellect, +1.0 * mul) },
                SpeechAct.Invite => bd with { Physical = BumpD(bd.Physical, +0.5 * mul) },
                SpeechAct.Boundary => bd with { Values = BumpD(bd.Values, -1.0 * mul) },
                _ => bd
            };
        }

        #endregion Private methods — DomainBreakdown

        #region Private methods — signal deltas

        /// <summary>
        /// Computes the total familiarity bonus produced by repeated accepted interactions.
        /// </summary>
        private static double MereExposureBoost(int count, RelationshipsConfig config)
        {
            if (count <= 0)
            {
                return 0.0;
            }

            var saturation = Math.Log(1.0 + count) / Math.Log(1.0 + config.MereExposureSaturation);
            return Math.Clamp(saturation * config.MereExposureMaxBoost, 0.0, config.MereExposureMaxBoost);
        }

        /// <summary>
        /// Computes a context-sensitive romantic-interest gain.
        /// Trust, comfort, closeness, like, and value alignment matter more than raw appearance.
        /// </summary>
        private static double ComputeRomanticInterestDelta(RelationshipEdge e)
            => ((Math.Max(0.0, e.Trust - 50.0) / 50.0) * 1.3)
             + ((Math.Max(0.0, e.Comfort - 45.0) / 55.0) * 0.9)
             + ((Math.Max(0.0, e.Closeness - 40.0) / 60.0) * 1.1)
             + ((Math.Max(0.0, e.Like - 45.0) / 55.0) * 0.6)
             + ((Math.Max(0.0, e.Breakdown.Values - 50.0) / 50.0) * 0.8);

        /// <summary>
        /// Computes a context-sensitive sexual-interest gain.
        /// Physical and aesthetic attraction lead, while comfort and closeness act as gates.
        /// </summary>
        private static double ComputeSexualInterestDelta(RelationshipEdge e)
            => ((Math.Max(0.0, e.PhysicalAttraction - 45.0) / 55.0) * 0.8)
             + ((Math.Max(0.0, e.AestheticAttraction - 45.0) / 55.0) * 0.6)
             + ((Math.Max(0.0, e.Comfort - 45.0) / 55.0) * 0.3)
             + ((Math.Max(0.0, e.Closeness - 40.0) / 60.0) * 0.25);

        /// <summary>
        /// Converts a change in accepted interaction count into an incremental familiarity delta.
        /// </summary>
        private double ComputeFamiliarityExposureDelta(int previousCount, int newCount)
            => MereExposureBoost(newCount, Config) - MereExposureBoost(previousCount, Config);

        #endregion Private methods — signal deltas

        #region Private methods — helpers

        /// <summary>
        /// Ensures an edge exists for <paramref name="other"/> without applying any mutation.
        /// Separated from <see cref="Upsert"/> for clarity in switch branches.
        /// </summary>
        private void EnsureEdge(HumanId self, HumanId other)
        {
            if (!State.Edges.ContainsKey(other))
            {
                Upsert(self, other, e => e);
            }
        }

        /// <summary>
        /// Maps touch intensity to a physical-domain reinforcement.
        /// </summary>
        private static double TouchBoost(TouchLevel level) => level switch
        {
            TouchLevel.Light => +1.5,
            TouchLevel.Friendly => +3.0,
            TouchLevel.Intimate => +5.0,
            _ => 0.0
        };

        /// <summary>
        /// Inserts or updates an edge in the relationship graph.
        /// If the edge does not exist, initialises it with neutral default values.
        /// </summary>
        private void Upsert(HumanId self, HumanId other, Func<RelationshipEdge, RelationshipEdge> mut)
        {
            if (self == other)
            {
                return;
            }

            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);

            if (!dict.TryGetValue(other, out var e))
            {
                e = new RelationshipEdge(
                    A: self,
                    B: other,
                    Like: 45,
                    Trust: 45,
                    Familiarity: 10,
                    AestheticAttraction: 35,
                    PhysicalAttraction: 35,
                    RomanticInterest: 35,
                    SexualInterest: 30,
                    Closeness: 10,
                    Respect: 55,
                    Comfort: 40,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 0);

                using (_log.BeginScope(new CharacterLogScope(self.Value, nameof(DefaultRelationshipsEngine))))
                {
                    _log.RelEdgeCreated(self.Value.ToString(), self.Value.ToString(), other.Value.ToString());
                }
            }

            var updated = mut(e);

            using (_log.BeginScope(new CharacterLogScope(self.Value, nameof(DefaultRelationshipsEngine))))
            {
                _log.RelEdgeUpdated(
                    self.Value.ToString(), self.Value.ToString(), other.Value.ToString(),
                    updated.Like, updated.Trust, updated.Closeness,
                    updated.Comfort, updated.Respect);
            }

            dict[other] = updated;
            State = new RelationshipState(dict);
        }

        /// <summary>Bumps a primary relationship dimension by <paramref name="by"/> and clamps to [0, 100].</summary>
        private static double Bump(double v, double by)
            => Math.Max(0, Math.Min(100, v + by));

        /// <summary>Bumps a domain breakdown value by <paramref name="by"/> and clamps to [0, 100].</summary>
        private static double BumpD(double v, double by)
            => Math.Max(0, Math.Min(100, v + by));

        /// <summary>Linear interpolation used for gradual blending such as first impressions.</summary>
        private static double Lerp(double a, double b, double t)
            => a + (b - a) * Math.Clamp(t, 0, 1);

        /// <summary>Clamps a value to [0, 100].</summary>
        private static double Clamp(double v)
            => Math.Max(0, Math.Min(100, v));

        /// <summary>
        /// Drifts <paramref name="cur"/> toward <paramref name="target"/> by at most <paramref name="amount"/>.
        /// Models forgetting without contact.
        /// </summary>
        private static double Approach(double cur, double target, double amount)
            => cur < target
                ? Math.Min(target, cur + amount)
                : Math.Max(target, cur - amount);

        #endregion Private methods — helpers
    }
}
