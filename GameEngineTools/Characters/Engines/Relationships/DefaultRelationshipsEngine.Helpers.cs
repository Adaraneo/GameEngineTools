// DefaultRelationshipsEngine.Helpers.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;

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
                SpeechAct.Invite when accepted => bd with { Physical = BumpD(bd.Physical, +0.5) },
                SpeechAct.Invite => bd,
                // Accepted boundary: signals self-respect → raises Values alignment.
                // Rejected boundary (mul = 0.5): values conflict → stronger penalty.
                SpeechAct.Boundary when accepted  => bd with { Values = BumpD(bd.Values, +0.8) },
                SpeechAct.Boundary                => bd with { Values = BumpD(bd.Values, -1.5 * mul) },
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
        private static double ComputeRomanticInterestDelta(
            RelationshipEdge e,
            SpeechAct act,
            Sociosexuality sociosexuality,
            AttractionProfile? attractionProfile,
            SexBiology? targetBiology)
        {
            var context = ((Math.Max(0.0, e.Trust - 50.0) / 50.0) * 1.3)
                 + ((Math.Max(0.0, e.Comfort - 45.0) / 55.0) * 0.9)
                 + ((Math.Max(0.0, e.Closeness - 25.0) / 75.0) * 1.1)
                 + ((Math.Max(0.0, e.Like - 45.0) / 55.0) * 0.6)
                 + ((Math.Max(0.0, e.Breakdown.Values - 50.0) / 50.0) * 0.8);

            var delta = act switch
            {
                SpeechAct.SelfDisclosure => context * 0.7,
                SpeechAct.Validation => context * 0.45,
                SpeechAct.Meta => context * 0.35,
                SpeechAct.Invite => context * 0.3,
                _ => 0
            };

            var orientedDelta = delta * SexualOrientationBehaviorMath.RomanticInterestMultiplier(attractionProfile, targetBiology);

            return act == SpeechAct.Invite
                ? orientedDelta * SociosexualityBehaviorMath.RomanticInviteDeltaMultiplier(sociosexuality)
                : orientedDelta;
        }

        /// <summary>
        /// Computes a context-sensitive sexual-interest gain.
        /// Physical and aesthetic attraction lead, while comfort and closeness act as gates.
        /// </summary>
        private static double ComputeSexualInterestDelta(
            RelationshipEdge e,
            SpeechAct act,
            Sociosexuality sociosexuality,
            AttractionProfile? attractionProfile,
            SexBiology? targetBiology)
        {
            var context = ((Math.Max(0, e.PhysicalAttraction - 50) / 50) * 0.9)
                + ((Math.Max(0, e.AestheticAttraction - 50) / 50) * 0.6);

            var gate = ((Math.Max(0, e.Comfort - 40) / 60) * 0.35)
                + ((Math.Max(0, e.Closeness - 20) / 80) * 0.25);

            // Coolidge efekt — novelty bonus u nového partnera (Baumeister et al. 2001).
            // Klesá logaritmicky s počtem interakcí; symetrický protějšek ke Coolidge decay.
            var noveltyMult = e.PositiveInteractionCount <= 0
                ? 1.8
                : Math.Max(1.0, 1.8 - Math.Log(1.0 + e.PositiveInteractionCount) * 0.35);

            var delta = act switch
            {
                SpeechAct.Invite => (context + gate) * 0.18 * noveltyMult,
                _ => 0
            };

            var orientedDelta = delta * SexualOrientationBehaviorMath.SexualInterestMultiplier(attractionProfile, targetBiology);

            return act == SpeechAct.Invite
                ? orientedDelta * SociosexualityBehaviorMath.SexualInterestDeltaMultiplier(sociosexuality)
                : orientedDelta;
        }

        /// <summary>
        /// Converts a change in accepted interaction count into an incremental familiarity delta.
        /// </summary>
        private double ComputeFamiliarityExposureDelta(int previousCount, int newCount)
            => MereExposureBoost(newCount, Config) - MereExposureBoost(previousCount, Config);

        /// <summary>
        /// Produces small attraction plasticity from accumulated safe or costly relational experience.
        /// Attraction remains mostly stable; this only models repeated relational colouring.
        /// </summary>
        private double ComputeAttractionPlasticity(RelationshipEdge edge, bool positive, SpeechAct act)
        {
            var amount = Math.Clamp(Config.AttractionPlasticityPerInteraction, 0.0, 1.0);
            var exposureDamping = 1.0 / Math.Sqrt(1.0 + Math.Max(0, edge.PositiveInteractionCount) * 0.15);

            if (positive)
            {
                var safety = Math.Clamp(
                    Math.Max(0.0, edge.Trust - 45.0) / 55.0 * 0.40
                    + Math.Max(0.0, edge.Comfort - 45.0) / 55.0 * 0.40
                    + Math.Max(0.0, edge.Like - 45.0) / 55.0 * 0.20,
                    0.0,
                    1.0);
                var actScale = act is SpeechAct.Validation or SpeechAct.SelfDisclosure ? 1.10 : 0.75;
                return amount * exposureDamping * safety * actScale;
            }

            var cost = Math.Clamp(
                Math.Max(0.0, 55.0 - edge.Trust) / 55.0 * 0.35
                + Math.Max(0.0, 55.0 - edge.Comfort) / 55.0 * 0.45
                + Math.Max(0.0, 50.0 - edge.Like) / 50.0 * 0.20,
                0.20,
                1.0);
            var rejectionScale = act == SpeechAct.Invite ? 1.25 : 0.85;

            return -amount * exposureDamping * cost * rejectionScale;
        }

        /// <summary>
        /// Converts repeated accepted contact into a small safety consolidation signal.
        /// Uses smooth exposure and current relationship quality; it is intentionally not a threshold gate.
        /// </summary>
        private double ComputeRelationalStabilization(RelationshipEdge edge, PsychologicalProfile? profile)
        {
            var exposure = Math.Clamp(
                Math.Log(1.0 + edge.PositiveInteractionCount) / Math.Log(1.0 + Config.MereExposureSaturation),
                0.0,
                1.0);
            var relationshipSafety =
                Math.Max(0.0, edge.Trust - 45.0) / 55.0 * 0.35
                + Math.Max(0.0, edge.Comfort - 42.0) / 58.0 * 0.40
                + edge.Closeness / 100.0 * 0.25;
            var ambivalenceGain = 0.85 + Math.Clamp(profile?.Ambivalence ?? PsychologicalProfile.Default.Ambivalence, 0.0, 1.0) * 0.30;

            return Math.Clamp(exposure * (0.45 + Math.Clamp(relationshipSafety, 0.0, 1.0) * 0.55) * ambivalenceGain, 0.0, 1.0);
        }

        /// <summary>
        /// Lets established safe contact soften, but not erase, the sting of a later rejection.
        /// Ambivalent characters and high-anxiety (preoccupied) attachment retain more sensitivity.
        /// </summary>
        /// <param name="attachment">
        /// Continuous ECR-R attachment profile; Anxiety amplifies the sting
        /// (hyperactivation strategy — Mikulincer &amp; Shaver 2016).
        /// </param>
        private double ComputeRejectionStingMultiplier(
            RelationshipEdge edge,
            PsychologicalProfile? profile,
            AttachmentProfile? attachment = null)
        {
            var exposure = Math.Clamp(
                Math.Log(1.0 + edge.PositiveInteractionCount) / Math.Log(1.0 + Config.MereExposureSaturation),
                0.0,
                1.0);
            var safety = Math.Clamp(
                Math.Max(0.0, edge.Trust - 50.0) / 50.0 * 0.35
                + Math.Max(0.0, edge.Comfort - 50.0) / 50.0 * 0.40
                + edge.Closeness / 100.0 * 0.25,
                0.0,
                1.0);
            var sensitivity = Math.Clamp(profile?.Ambivalence ?? PsychologicalProfile.Default.Ambivalence, 0.0, 1.0);
            var followThrough = Math.Clamp(profile?.FollowThrough ?? PsychologicalProfile.Default.FollowThrough, 0.0, 1.0);
            var protection = exposure * safety * (0.30 + followThrough * 0.35) * (1.0 - sensitivity * 0.45);

            var baseMultiplier = Math.Clamp(1.0 - protection, 0.72, 1.0);

            // Attachment anxiety amplifies rejection impact (hyperactivation strategy).
            // At Anxiety = 1.0, sting is multiplied by (1 + RejectionAnxietyAmplifier).
            var anxietyBoost = 1.0 + (attachment?.Anxiety ?? 0.0) * Config.RejectionAnxietyAmplifier;

            return Math.Clamp(baseMultiplier * anxietyBoost, 0.72, 1.0 + Config.RejectionAnxietyAmplifier);
        }

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
        /// Returns the minimum accumulated social-decay interval in days for the current runtime fidelity.
        /// Full = every tick, Reduced = every 12 hours, Minimal = every 24 hours.
        /// </summary>
        private static double GetSocialDecayCadenceDays(SocialFidelityLevel fidelity)
            => fidelity switch
            {
                SocialFidelityLevel.Full => 0.0,
                SocialFidelityLevel.Reduced => 0.5,
                SocialFidelityLevel.Minimal => 1.0,
                _ => 0.0
            };

        /// <summary>
        /// Determines whether the currently accumulated decay budget is large enough to process
        /// relationship drift for the current fidelity tier.
        /// </summary>
        private static bool ShouldApplySocialDecay(double accumulatedDays, SocialFidelityLevel fidelity)
        {
            var cadenceDays = GetSocialDecayCadenceDays(fidelity);
            return cadenceDays <= 0.0 || accumulatedDays >= cadenceDays;
        }

        /// <summary>
        /// Creates a neutral directed edge for a newly known target.
        /// </summary>
        private static RelationshipEdge CreateDefaultEdge(HumanId self, HumanId other)
            => new(
                A: self,
                B: other,
                Like: 50,
                Trust: 50,
                Familiarity: 0,
                AestheticAttraction: 0,
                PhysicalAttraction: 0,
                RomanticInterest: 0,
                SexualInterest: 0,
                Closeness: 0,
                Respect: 50,
                Comfort: 45,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                PositiveInteractionCount: 0);

        /// <summary>
        /// Produces a compact before/after diff for relationship-relevant fields.
        /// </summary>
        private static string DescribeEdgeChanges(RelationshipEdge before, RelationshipEdge after)
        {
            var changes = new List<string>();

            AppendChange(changes, nameof(RelationshipEdge.Like), before.Like, after.Like);
            AppendChange(changes, nameof(RelationshipEdge.Trust), before.Trust, after.Trust);
            AppendChange(changes, nameof(RelationshipEdge.Familiarity), before.Familiarity, after.Familiarity);
            AppendChange(changes, nameof(RelationshipEdge.Closeness), before.Closeness, after.Closeness);
            AppendChange(changes, nameof(RelationshipEdge.Comfort), before.Comfort, after.Comfort);
            AppendChange(changes, nameof(RelationshipEdge.Respect), before.Respect, after.Respect);
            AppendChange(changes, nameof(RelationshipEdge.AestheticAttraction), before.AestheticAttraction, after.AestheticAttraction);
            AppendChange(changes, nameof(RelationshipEdge.PhysicalAttraction), before.PhysicalAttraction, after.PhysicalAttraction);
            AppendChange(changes, nameof(RelationshipEdge.RomanticInterest), before.RomanticInterest, after.RomanticInterest);
            AppendChange(changes, nameof(RelationshipEdge.SexualInterest), before.SexualInterest, after.SexualInterest);

            if (before.PositiveInteractionCount != after.PositiveInteractionCount)
            {
                changes.Add($"{nameof(RelationshipEdge.PositiveInteractionCount)}:{before.PositiveInteractionCount}->{after.PositiveInteractionCount}");
            }

            if (!changes.Any())
            {
                return "none";
            }

            return string.Join(", ", changes);
        }

        /// <summary>
        /// Appends a numeric field diff when the value changed materially.
        /// </summary>
        private static void AppendChange(List<string> changes, string field, double before, double after)
        {
            if (Math.Abs(before - after) < 0.001)
            {
                return;
            }

            changes.Add($"{field}:{before:F1}->{after:F1}");
        }

        /// <summary>
        /// Maps touch intensity to a physical-domain reinforcement.
        /// </summary>
        private static double TouchBoost(TouchLevel level) => level switch
        {
            TouchLevel.Light => +0.1,
            TouchLevel.Friendly => +0.8,
            TouchLevel.Intimate => +3.5,
            _ => 0.0
        };

        /// <summary>
        /// Resolves the biology of the person opposite <paramref name="self"/> in a two-person event.
        /// </summary>
        private static SexBiology? ResolveOtherBiology(
            HumanId self,
            HumanId first,
            SexBiology? firstBiology,
            HumanId second,
            SexBiology? secondBiology)
        {
            if (self == first)
            {
                return secondBiology;
            }

            return self == second ? firstBiology : null;
        }

        /// <summary>
        /// Inserts or updates an edge in the relationship graph.
        /// If the edge does not exist, initialises it with neutral default values.
        /// </summary>
        /// <param name="now">
        /// World-time of the triggering event; stored as <see cref="RelationshipEdge.LastContactTime"/>
        /// for the Navarro 8× gap rule. Pass <c>null</c> only for read-only probes that should not
        /// reset the contact clock.
        /// </param>
        private void Upsert(
            HumanId self,
            HumanId other,
            Func<RelationshipEdge, RelationshipEdge> mut,
            string? eventType = null,
            string? outcome = null,
            string? detail = null,
            WDateTime? now = null)
        {
            if (self == other)
            {
                return;
            }

            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);

            if (!dict.TryGetValue(other, out var e))
            {
                e = CreateDefaultEdge(self, other);

                using (_log.BeginCharacterScope(self.Value, nameof(DefaultRelationshipsEngine), relatedPersonId: other.Value))
                {
                    _log.RelEdgeCreated(self.Value.ToString(), self.Value.ToString(), other.Value.ToString());
                }
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                using (_log.BeginCharacterScope(self.Value, nameof(DefaultRelationshipsEngine), relatedPersonId: other.Value))
                {
                    _log.RelEventReceived(
                        self.Value.ToString(),
                        eventType,
                        self.Value.ToString(),
                        other.Value.ToString(),
                        outcome ?? "n/a",
                        detail ?? "n/a");
                }
            }

            var updated = mut(e);

            // Update LastContactTime for Navarro gap rule tracking.
            if (now.HasValue)
            {
                updated = updated with { LastContactTime = now.Value };
            }

            using (_log.BeginCharacterScope(self.Value, nameof(DefaultRelationshipsEngine), relatedPersonId: other.Value))
            {
                if (!string.IsNullOrWhiteSpace(eventType))
                {
                    _log.RelEventApplied(
                        self.Value.ToString(),
                        eventType,
                        self.Value.ToString(),
                        other.Value.ToString(),
                        outcome ?? "n/a",
                        DescribeEdgeChanges(e, updated));
                }

                _log.RelEdgeUpdated(
                    self.Value.ToString(), self.Value.ToString(), other.Value.ToString(),
                    updated.Like, updated.Trust, updated.Closeness,
                    updated.Comfort, updated.Respect, updated.Familiarity, updated.RomanticInterest, updated.SexualInterest, updated.AestheticAttraction, updated.PhysicalAttraction);
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

        #region Private methods — third-party gossip

        /// <summary>
        /// Emits <see cref="ThirdPartyActionObserved"/> events for each observer present
        /// at the scene of a MicroPositive or MicroNegative.
        /// Observers are sourced from <see cref="Interactions.InteractionSurface.Observers"/>;
        /// skips the direct participants (self / actor / target).
        /// </summary>
        private static void EmitThirdPartyEvents(
            WDateTime occurredAt,
            HumanId self,
            HumanId actor,
            HumanId target,
            ThirdPartyObservationType type,
            double valence,
            System.Collections.Generic.IReadOnlyList<HumanId>? observers,
            IEventCollector outbox)
        {
            if (observers is not { Count: > 0 }) return;

            foreach (var observer in observers)
            {
                // Skip direct participants — they process the original event themselves
                if (observer == self || observer == actor || observer == target) continue;

                outbox.Add(new ThirdPartyActionObserved(
                    occurredAt,
                    Observer: observer,
                    Actor: actor,
                    Target: target,
                    Valence: valence,
                    Type: type));
            }
        }

        #endregion Private methods — third-party gossip
    }
}
