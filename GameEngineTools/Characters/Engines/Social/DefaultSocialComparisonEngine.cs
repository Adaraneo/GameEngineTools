// DefaultSocialComparisonEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Social
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default implementation of <see cref="ISocialComparisonEngine"/>. On a throttled reflective
    /// cadence it selects a reference peer from the relationship graph, evaluates the
    /// contrast/assimilation reaction against them, and emits a <see cref="SocialComparisonOccurred"/>
    /// carrying the deltas that SelfConcept, Psychology and Relationships apply next tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Target selection</b> follows the empirical default: people prefer <i>upward</i> standards
    /// even under threat (Gerber, Wheeler &amp; Suls 2018), so the most-admired known peer is the
    /// salient standard. The documented exception is mood repair — a low-self-esteem comparer in a
    /// negative mood selects a <i>downward</i> standard instead (Wills 1981).
    /// </para>
    /// <para>
    /// The engine reads the committed previous-tick snapshot (self-concept + relationships) via
    /// <see cref="IHumanContext"/> rather than any engine's live mid-tick state, consistent with the
    /// pipeline contract. All numerical reasoning lives in <see cref="SocialComparisonMath"/>.
    /// </para>
    /// </remarks>
    internal sealed class DefaultSocialComparisonEngine : ISocialComparisonEngine
    {
        #region State and configuration

        /// <inheritdoc/>
        public SocialComparisonState State { get; private set; } = new();

        /// <inheritdoc/>
        public SocialComparisonConfig Config { get; }

        #endregion State and configuration

        #region Private fields

        private readonly ILogger _log;

        #endregion Private fields

        #region Construction

        public DefaultSocialComparisonEngine(IOptions<SocialComparisonConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultSocialComparisonEngine>();
        }

        #endregion Construction

        #region IEngine — Tick

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            if (Math.Max(0.0, dt.TotalDays) <= 0.0)
                return;

            // Throttle to a reflective cadence — comparison is not a per-tick act.
            if (State.LastComparisonAt is { } last
                && WDateTime.Difference(now, last).TotalDays < Config.ComparisonCooldownDays)
                return;

            var snap = ctx.Snapshot;
            var selfConcept = snap.SelfConcept;
            var relationships = snap.Relationships;
            if (selfConcept is null || relationships is null || relationships.Edges.Count == 0)
                return;

            var selfEsteem = selfConcept.SelfEsteem;
            var selfStanding = selfEsteem * 100.0;

            // Mood-repair selection: a low-self-esteem comparer in a negative mood seeks a downward
            // standard; otherwise the most-admired peer (upward) is the salient standard.
            var seekDownward = selfEsteem < 0.5 && snap.Psychology.Valence < 0.0;

            if (!TrySelectTarget(relationships.Edges, selfStanding, seekDownward, out var targetId, out var targetStanding, out var closeness))
                return;

            // From this point a comparison "occurs" — arm the cooldown even if the result is inert.
            State = State with { LastComparisonAt = now };

            var bf = ctx.Personality.BigFive;
            var result = SocialComparisonMath.Evaluate(
                selfStanding, targetStanding, closeness,
                bf.Neuroticism, bf.Agreeableness, selfEsteem, Config);

            if (result.IsNegligible)
                return;

            outbox.Add(new SocialComparisonOccurred(
                now, ctx.Id, targetId,
                result.Direction, result.Reaction, result.Envy,
                result.SelfEsteemDelta, result.MoodValenceDelta, result.MoodBaselineDelta,
                result.AchievementMotivationDelta, result.TargetHostilityDelta));

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSocialComparisonEngine), relatedPersonId: targetId.Value))
            {
                _log.SocialComparisonOccurredLog(
                    ctx.Id.Value.ToString(),
                    targetId.Value.ToString(),
                    result.Direction.ToString(),
                    result.Reaction.ToString(),
                    result.Envy.ToString(),
                    result.SelfEsteemDelta,
                    result.MoodValenceDelta,
                    result.MoodBaselineDelta,
                    result.AchievementMotivationDelta,
                    result.TargetHostilityDelta);
            }
        }

        #endregion IEngine — Tick

        #region IEngine — Handle / RestoreState

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            // Social comparison is self-generated from snapshot state; it consumes no inbound events.
        }

        /// <inheritdoc/>
        public void RestoreState(SocialComparisonState state) => State = state;

        #endregion IEngine — Handle / RestoreState

        #region Target selection

        /// <summary>
        /// Picks the salient comparison standard among known peers. Upward mode chooses the
        /// highest-standing eligible peer; downward mode the lowest-standing peer that is below the self.
        /// </summary>
        private bool TrySelectTarget(
            IReadOnlyDictionary<HumanId, RelationshipEdge> edges,
            double selfStanding,
            bool seekDownward,
            out HumanId targetId,
            out double targetStanding,
            out double closeness)
        {
            targetId = default;
            targetStanding = 0.0;
            closeness = 0.0;

            var found = false;
            var bestStanding = seekDownward ? double.MaxValue : double.MinValue;

            foreach (var (id, edge) in edges)
            {
                if (edge.Familiarity < Config.MinFamiliarity)
                    continue;

                var standing = Standing(edge);

                if (seekDownward)
                {
                    // Only peers below the self qualify for a mood-repair downward comparison.
                    if (standing >= selfStanding || standing >= bestStanding)
                        continue;
                }
                else if (standing <= bestStanding)
                {
                    continue;
                }

                bestStanding = standing;
                targetId = id;
                targetStanding = standing;
                closeness = edge.Closeness;
                found = true;
            }

            return found;
        }

        private double Standing(RelationshipEdge edge)
            => Config.StandingRespectWeight * edge.Respect
             + (1.0 - Config.StandingRespectWeight) * edge.PerceivedPrestige;

        #endregion Target selection
    }
}
