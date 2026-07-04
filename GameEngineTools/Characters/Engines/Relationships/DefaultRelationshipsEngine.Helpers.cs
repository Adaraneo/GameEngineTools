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
                SpeechAct.Boundary when accepted => bd with { Values = BumpD(bd.Values, +0.8) },
                SpeechAct.Boundary => bd with { Values = BumpD(bd.Values, -1.5 * mul) },
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

            // Coolidge effect — a novelty bonus for a new partner (Baumeister et al. 2001).
            // Decreases logarithmically with the number of interactions; the symmetric counterpart to Coolidge decay.
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

        #region Private methods — investment model (Rusbult)

        /// <summary>
        /// Computes CL_alt for a romantic edge: the best available alternative's romantic potential,
        /// scanned across the owner's other edges. Returns 0 for non-romantic edges.
        /// Attraction dimensions do not decay, so a latent alternative persists over time.
        /// </summary>
        /// <param name="edge">The edge whose AlternativeQuality is being computed.</param>
        /// <param name="allEdges">All edges in the owner's graph.</param>
        /// <param name="cfg">Engine configuration.</param>
        private static double ComputeAlternativeQuality(
            RelationshipEdge edge,
            IReadOnlyDictionary<HumanId, RelationshipEdge> allEdges,
            RelationshipsConfig cfg)
        {
            // Only romantic bonds have a meaningful "better partner available" pressure.
            var isRomantic = edge.KinRole == KinRole.Partner
                          || edge.IntimateAffinity >= cfg.RomanticEdgeIntimacyThreshold;
            if (!isRomantic)
            {
                return 0.0;
            }

            var best = 0.0;
            foreach (var other in allEdges.Values)
            {
                if (other.B == edge.B)
                {
                    continue; // skip the partner themselves
                }

                // Romantic potential of an alternative = attraction blend, gated by some familiarity.
                var potential = other.AestheticAttraction * 0.35
                              + other.PhysicalAttraction * 0.35
                              + other.IntimateAffinity * 0.30;
                if (potential > best)
                {
                    best = potential;
                }
            }

            return Math.Clamp(best, 0.0, 100.0);
        }

        /// <summary>
        /// Computes the commitment target from the Rusbult integrator:
        /// satisfaction + investment·w − alternatives·w. Current Commitment drifts toward this.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Predictor covariance (research gate, resolved):</b> satisfaction, investment and
        /// alternatives are computed independently here, which matches the Investment Model's
        /// theoretical structure (Rusbult; Le &amp; Agnew 2003; Tran et al. 2019). In practice the three
        /// predictors correlate substantially (Joel, MacDonald et al. 2015, <i>Journal of Social and
        /// Personal Relationships</i>: r ≈ −.44 to .42 across the Le &amp; Agnew meta-analytic sample,
        /// attributed to a shared perceived-partner-responsiveness factor). This is expected covariance
        /// from shared upstream signals (Like/Closeness/Comfort feed both Satisfaction and, indirectly,
        /// InvestmentSize growth) — not a defect to be "decorrelated".
        /// </para>
        /// <para>
        /// <b>Relational vs. non-relational domain differentiation (research gate, resolved):</b> Le &amp;
        /// Agnew (2003) found the Investment Model holds more strongly in relational domains than in
        /// non-relational ones (friendship, work, community). <see cref="ComputeAlternativeQuality"/>
        /// already gates CL_alt to romantic edges (<see cref="KinRole.Partner"/> or an intimacy
        /// threshold) — a reasonable proxy, since CL_alt (competing partners) is theoretically
        /// near-meaningless for platonic bonds. <see cref="RelationshipsConfig.InvestmentGrowthPerDay"/>
        /// and <see cref="RelationshipsConfig.ComparisonLevelBaseline"/> apply uniformly across all
        /// <see cref="KinRole"/> values — verified deliberate, not an oversight: the literature reports
        /// only a qualitative "significantly stronger" effect with no citable numeric ratio, so adding a
        /// differentiation multiplier here would violate GET's citation-gate policy (no uncited constants).
        /// </para>
        /// <para>
        /// <b>Conflict-trajectory term (Topic D, Task D.5):</b> Source: Kanter, Lavner, Lannin, Hilgard &amp;
        /// Monk (2022, <i>JMF</i>, 84(2), 533–551) — negativity → dissolution d=−0.41, negativity →
        /// relational quality r=−.17. The authors explicitly caution these are SMALL effects and single
        /// assessments are only modestly predictive. <see cref="RelationshipsConfig.CommitmentConflictTrajectoryWeight"/>
        /// is therefore kept deliberately small (default 0.15, well below
        /// <see cref="RelationshipsConfig.CommitmentAlternativeWeight"/>'s 0.5) so a high
        /// <see cref="RelationshipEdge.DemandWithdrawScore"/> nudges Commitment down without dominating
        /// the satisfaction/investment/alternatives terms that carry the strongest (r≈.65-.68) empirical support.
        /// </para>
        /// </remarks>
        private static double ComputeCommitmentTarget(RelationshipEdge edge, RelationshipsConfig cfg)
        {
            // Satisfaction = Outcomes − CL (Thibaut & Kelley 1959).
            var outcomes = (edge.Like + edge.Closeness + edge.Comfort) / 3.0;
            var satisfaction = outcomes - cfg.ComparisonLevelBaseline;

            var conflictPenalty = (edge.DemandWithdrawScore / 100.0) * cfg.CommitmentConflictTrajectoryWeight;

            var target = satisfaction
                       + edge.InvestmentSize * cfg.CommitmentInvestmentWeight
                       - edge.AlternativeQuality * cfg.CommitmentAlternativeWeight
                       - conflictPenalty * 100.0;

            return Math.Clamp(target, 0.0, 100.0);
        }

        #endregion Private methods — investment model (Rusbult)

        #region Private methods — jealousy (Buss et al. 1992; Dijkstra & Buunk 1998; Pollet & Saxton 2020)

        /// <summary>
        /// Rival-attractiveness modulator for jealousy intensity: a more attractive rival intensifies
        /// the reaction, gated by sex per the confirmed replication.
        /// </summary>
        /// <remarks>
        /// Source: Dijkstra &amp; Buunk (1998, <i>PSPB</i>, 24(11), 1158–1166) — original vignette
        /// finding; Pollet &amp; Saxton (2020, <i>PSPB</i>, 46(10), 1428–1443) — two large replications
        /// (combined N=5,899/4,038) CONFIRM the rival-attractiveness × sex interaction; they do NOT
        /// confirm a rival-dominance effect, which is therefore deliberately absent from this
        /// computation (rival dominance is an explicit rejection, not an oversight — do not add a
        /// PerceivedDominance-based modulator here).
        /// </remarks>
        /// <param name="rival">The rival's HumanId (tpa.Target).</param>
        /// <param name="allEdges">The observer's full edge graph.</param>
        /// <param name="observerSex">The observer's biological sex, for the sex-differentiated weighting.</param>
        /// <param name="cfg">Engine configuration.</param>
        /// <returns>Multiplier in roughly [0.7, 1.3] applied to base jealousy intensity.</returns>
        private static double ComputeRivalAttractivenessModifier(
            HumanId rival,
            IReadOnlyDictionary<HumanId, RelationshipEdge> allEdges,
            SexBiology observerSex,
            RelationshipsConfig cfg)
        {
            if (!allEdges.TryGetValue(rival, out var rivalEdge))
            {
                return 1.0; // no prior perception of the rival — neutral baseline
            }

            var rivalAttractiveness = Math.Clamp(
                (rivalEdge.AestheticAttraction + rivalEdge.PhysicalAttraction) / 2.0 / 100.0,
                0.0, 1.0);

            // Pollet & Saxton (2020): women's jealousy responds more strongly to rival attractiveness
            // than men's. Small effect — keep the sex differential modest.
            var sexWeight = observerSex == SexBiology.Female
                ? cfg.RivalAttractivenessWeightFemale
                : cfg.RivalAttractivenessWeightMale;

            return 1.0 + (rivalAttractiveness - 0.5) * sexWeight;
        }

        /// <summary>
        /// Applies a jealousy reaction to both the actor edge (partner-trust/distress content) and
        /// the rival edge (rival-directed hostility), shared by the IntimateAct and
        /// EmotionalIntimacyAct third-party observation branches so the two never drift out of sync.
        /// </summary>
        /// <remarks>
        /// Source: Buss, Larsen, Westen &amp; Semmelroth (1992) — sex-differentiated sensitivity to
        /// sexual vs. emotional infidelity, passed in via <paramref name="biasMultiplier"/>; Dijkstra
        /// &amp; Buunk (1998) and Pollet &amp; Saxton (2020) — rival attractiveness intensifies
        /// jealousy (rival dominance explicitly rejected — see
        /// <see cref="ComputeRivalAttractivenessModifier"/>). Only <see cref="JealousyType.Reactive"/>
        /// is generated here — see that type's remarks for why Suspicious jealousy has no generative
        /// mechanism in GET.
        /// <para>
        /// The rival-attractiveness modifier is folded into <c>jealousyIntensity</c> exactly once and
        /// then reused for both the partner-distress term (TransgressionResidue) and the
        /// rival-hostility term (Like/Respect) — it is deliberately not re-applied a second time to
        /// the rival-hostility term, which would silently square its effect.
        /// </para>
        /// </remarks>
        private void ApplyJealousyReaction(
            HumanId self,
            HumanId actor,
            HumanId target,
            IHumanContext ctx,
            double intimacyInterest,
            double biasMultiplier,
            JealousyType jealousyType,
            WDateTime occurredAt,
            IEventCollector outbox)
        {
            var anxiety = ctx.Personality.Attachment.Anxiety;
            var soiAttitude = ctx.Personality.Sociosexuality.Attitude;
            var rivalAttractivenessModifier = ComputeRivalAttractivenessModifier(target, State.Edges, ctx.Biology, Config);

            var jealousyBase = Math.Clamp(intimacyInterest / 100.0, 0, 1);
            var jealousyIntensity = jealousyBase * biasMultiplier * rivalAttractivenessModifier
                                  * (1.0 + anxiety * 0.8)
                                  * (1.0 - soiAttitude * 0.3);

            var transgressionAmount = Math.Clamp(jealousyIntensity * 12.0, 0, 20.0);
            var oldResidue = State.Edges.TryGetValue(actor, out var jealEdge) ? jealEdge.TransgressionResidue : 0.0;

            Upsert(self, actor, e => e with
            {
                TransgressionResidue = Math.Min(100, e.TransgressionResidue + transgressionAmount)
            },
            eventType: "JealousyDistress",
            outcome: $"distress={transgressionAmount:F1}",
            detail: $"actor={actor.Value}, intimacyInterest={intimacyInterest:F0}",
            now: occurredAt);

            using (_log.BeginCharacterScope(self.Value, nameof(DefaultRelationshipsEngine), relatedPersonId: actor.Value))
            {
                _log.JealousyDistressApplied(
                    self.Value.ToString(), jealousyType.ToString(), actor.Value.ToString(), target.Value.ToString(),
                    oldResidue, oldResidue + transgressionAmount);
            }

            // Rival-edge propagation (research gate, resolved: propagate to BOTH edges with
            // different content). The actor edge above absorbed partner-trust/distress content;
            // the rival edge absorbs hostility toward the third party.
            // Source: Buss et al. (1992); Dijkstra & Buunk (1998) — rival characteristics shape the
            // intensity of rival-directed hostility, distinct from partner-directed distress.
            var rivalHostility = Math.Clamp(jealousyIntensity * Config.RivalHostilityScale, 0.0, 20.0);
            if (rivalHostility > 0.0)
            {
                Upsert(self, target, e => e with
                {
                    Like = Bump(e.Like, -rivalHostility),
                    Respect = Bump(e.Respect, -rivalHostility * 0.5)
                },
                eventType: "JealousyRivalHostility",
                outcome: $"hostility={rivalHostility:F1}",
                detail: $"rival={target.Value}, viaActor={actor.Value}",
                now: occurredAt);

                using (_log.BeginCharacterScope(self.Value, nameof(DefaultRelationshipsEngine), relatedPersonId: target.Value))
                {
                    _log.JealousyRivalHostilityApplied(
                        self.Value.ToString(), target.Value.ToString(), actor.Value.ToString(), rivalHostility);
                }
            }
        }

        #endregion Private methods — jealousy

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
                IntimateAffinity: 0,
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
            AppendChange(changes, nameof(RelationshipEdge.IntimateAffinity), before.IntimateAffinity, after.IntimateAffinity);
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
                    updated.Comfort, updated.Respect, updated.Familiarity, updated.IntimateAffinity, updated.SexualInterest, updated.AestheticAttraction, updated.PhysicalAttraction,
                    updated.CommunalStrength, updated.ExchangeStrength, updated.TransgressionResidue,
                    updated.ResponsiveDesireLevel, updated.PerceivedDominance, updated.PerceivedPrestige,
                    updated.IsContemptuouslyDestroyed,
                    updated.Commitment, updated.InvestmentSize, updated.AlternativeQuality, updated.DissolutionConsidered);
            }

            CheckMilestones(self, e, updated);

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

        #region Private methods — decay logging helpers

        /// <summary>
        /// Produces a compact string listing relationship dimensions that changed by more than 0.05
        /// between <paramref name="before"/> and <paramref name="after"/>.
        /// </summary>
        private static string BuildDecayChangesString(RelationshipEdge before, RelationshipEdge after)
        {
            var parts = new System.Collections.Generic.List<string>();
            void Check(string name, double b, double a) { if (Math.Abs(a - b) > 0.05) parts.Add($"{name}:{b:F1}→{a:F1}"); }
            Check("Like", before.Like, after.Like);
            Check("Trust", before.Trust, after.Trust);
            Check("Closeness", before.Closeness, after.Closeness);
            Check("Familiarity", before.Familiarity, after.Familiarity);
            Check("IntimateAffinity", before.IntimateAffinity, after.IntimateAffinity);
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Checks whether any tracked relationship dimension crossed a milestone threshold
        /// (25, 50, 75, or 90) between <paramref name="before"/> and <paramref name="after"/>,
        /// and logs via <see cref="CoreLog.RelationshipMilestoneReached"/> when it did.
        /// </summary>
        private void CheckMilestones(HumanId self, RelationshipEdge before, RelationshipEdge after)
        {
            var milestones = new[] { 25.0, 50.0, 75.0, 90.0 };
            void CheckDim(string name, double b, double a)
            {
                foreach (var t in milestones)
                {
                    if (b < t && a >= t)
                    {
                        using (_log.BeginCharacterScope(self.Value, nameof(DefaultRelationshipsEngine)))
                        {
                            _log.RelationshipMilestoneReached(self.Value.ToString(), after.A.Value.ToString(), after.B.Value.ToString(), name, t, b, a);
                        }
                    }
                }
            }
            CheckDim("Like", before.Like, after.Like);
            CheckDim("Trust", before.Trust, after.Trust);
            CheckDim("Closeness", before.Closeness, after.Closeness);
            CheckDim("IntimateAffinity", before.IntimateAffinity, after.IntimateAffinity);
        }

        #endregion Private methods — decay logging helpers

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
