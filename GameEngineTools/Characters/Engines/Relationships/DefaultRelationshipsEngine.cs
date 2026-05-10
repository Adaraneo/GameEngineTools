// DefaultRelationshipsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default implementation of <see cref="IRelationshipsEngine"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Manages a directed relationship graph for one character.
    /// Each <see cref="RelationshipEdge"/> represents how this character perceives one other person.
    /// The graph is <b>asymmetric</b>: A may like B more than B likes A.
    /// </para>
    /// <para>
    /// <b>Three dynamic layers:</b>
    /// <list type="number">
    ///   <item><b>Events</b> — immediate jumps (MicroPositive, RepairAttempt, InteractionOutcome)</item>
    ///   <item><b>Decay</b> — slow drift toward neutral values (time without contact)</item>
    ///   <item><b>DomainBreakdown</b> — granular record of <em>what</em> the character values (humour, intellect…)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Mere-exposure effect:</b>
    /// Each accepted interaction increments <see cref="RelationshipEdge.PositiveInteractionCount"/>.
    /// A logarithmic bonus is applied to <see cref="RelationshipEdge.Familiarity"/>,
    /// saturating at <see cref="RelationshipsConfig.MereExposureSaturation"/> interactions.
    /// </para>
    /// </remarks>
    internal sealed partial class DefaultRelationshipsEngine : IRelationshipsEngine
    {
        #region State and configuration

        /// <inheritdoc/>
        public RelationshipState State { get; private set; }

        /// <inheritdoc/>
        public RelationshipsConfig Config { get; }

        #endregion State and configuration

        #region Private fields

        private readonly ILogger _log;
        private readonly ISocialFidelityPolicy _socialFidelityPolicy;
        private double _deferredDecayDays;

        #endregion Private fields

        #region Constructor

        /// <summary>Creates an engine instance with an empty relationship graph.</summary>
        public DefaultRelationshipsEngine(IOptions<RelationshipsConfig> cfg, ILoggerFactory loggerFactory, ISocialFidelityPolicy socialFidelityPolicy)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultRelationshipsEngine>();
            _socialFidelityPolicy = socialFidelityPolicy;
            _deferredDecayDays = 0;
            State = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>());
        }

        #endregion Constructor

        #region Handle — domain event processing

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            var self = ctx.Id;

            switch (@event)
            {
                // ── First impression ─────────────────────────────────────────────────────
                case FirstImpressionFormed fi:
                    {
                        var other = self == fi.A ? fi.B : self == fi.B ? fi.A : fi.B;
                        var otherBiology = ResolveOtherBiology(self, fi.A, fi.ABiology, fi.B, fi.BBiology);
                        if (other == self)
                        {
                            break;
                        }

                        Upsert(self, other, e =>
                        {
                            // A2 — Halo effect seeds Trust, Comfort, and Respect at first impression.
                            // Physically attractive people are perceived as more trustworthy, comfortable,
                            // and respectable on first contact (Eagly et al. 1991 meta-analysis).
                            // Weights applied above the neutral default only when attraction exceeds 50.
                            // Below 50: no halo bonus (unattractive target does not exceed neutral default).
                            var attraction = fi.Attraction;  // [0, 100]
                            var haloBonus  = Math.Clamp(attraction - 50.0, 0.0, 50.0); // 0..50

                            // Trust: small halo boost (easily overwritten by actual interaction)
                            var haloTrust   = e.Trust <= 0 ? 50.0 + haloBonus * 0.10 : e.Trust;
                            // Comfort: moderate boost above default (45) — attractive stranger feels safer
                            var haloComfort = Math.Max(e.Comfort, 45.0 + haloBonus * 0.40);
                            // Respect: above neutral (50) — attractive people are assumed more capable
                            var haloRespect = Math.Max(e.Respect, 50.0 + haloBonus * 0.35);

                            // A3 — Sexual interest seed from physical attractiveness (Regan & Berscheid 1999).
                            // Physical attractiveness triggers an immediate, involuntary lust component
                            // that is distinct from romantic interest and does not require prior interaction.
                            // The seed is orientation-weighted: a cross-orientation pair receives a
                            // proportionally smaller seed (SexualInterestMultiplier ≈ 0.05–1.0).
                            // Clamped to [0, 45]: first impression seeds only the tonic baseline zone;
                            // phasic passion (> TonicSexualInterestThreshold = 40) requires repeated
                            // intimate interaction and cannot be established on first sight alone.
                            var physicalAttractionNorm = fi.BasePhysical / 40.0 * 100.0;
                            var sexualInterestSeed = Math.Clamp(physicalAttractionNorm * Config.SexualInterestSeedFactor * SexualOrientationBehaviorMath.SexualInterestMultiplier(ctx.AttractionProfile, otherBiology), 0.0, 45.0);

                            return e with
                            {
                                // Lerp 70% toward the first impression — does not fully override
                                // if the character already knows the person slightly.
                                Like   = Lerp(e.Like, fi.Like, 0.7),
                                Familiarity = Math.Max(e.Familiarity, 2),
                                Trust  = Clamp(haloTrust),
                                Comfort = Clamp(haloComfort),
                                Respect = Clamp(haloRespect),
                                Closeness = Math.Max(e.Closeness, 0),
                                AestheticAttraction = Lerp(e.AestheticAttraction, fi.PreferenceMatch / 35.0 * 100.0, 0.8),
                                PhysicalAttraction  = Lerp(e.PhysicalAttraction,  fi.BasePhysical    / 40.0 * 100.0, 0.8),
                                RomanticInterest = e.RomanticInterest,
                                SexualInterest   = Math.Max(e.SexualInterest, sexualInterestSeed),
                                Breakdown = e.Breakdown with
                                {
                                    Physical  = Lerp(e.Breakdown.Physical,   fi.BasePhysical    / 40.0 * 100.0, 0.8),
                                    Aesthetics = Lerp(e.Breakdown.Aesthetics, fi.PreferenceMatch / 35.0 * 100.0, 0.8)
                                },
                                TargetBiology = otherBiology ?? e.TargetBiology
                            };
                        },
                        eventType: nameof(FirstImpressionFormed),
                        outcome: "formed",
                        detail: $"source={fi.A.Value}, like={fi.Like:F1}, basePhysical={fi.BasePhysical:F1}, preferenceMatch={fi.PreferenceMatch:F1}",
                        now: fi.OccurredAt);

                        using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultRelationshipsEngine)))
                        {
                            _log.RelFirstImpression(
                                ctx.Id.Value.ToString(),
                                fi.A.Value.ToString(), fi.B.Value.ToString(),
                            fi.Like);
                        }

                        break;
                    }

                // ── Micro-positive (smile, compliment, small help…) ──────────────────────
                case MicroPositive mp:
                    Upsert(self, mp.B, e => e with
                    {
                        Like = Bump(e.Like, +2.0),
                        Trust = Bump(e.Trust, +1.0),
                        Closeness = Bump(e.Closeness, +1.5),
                        Comfort = Bump(e.Comfort, +2.0)
                    },
                    eventType: nameof(MicroPositive),
                    outcome: "positive",
                    detail: mp.Kind ?? "n/a",
                    now: mp.OccurredAt);
                    // R1 — Third-party gossip: observers update their edge toward the actor (mp.B)
                    EmitThirdPartyEvents(mp.OccurredAt, self, mp.B, mp.A,
                        ThirdPartyObservationType.PositiveAct, valence: +1.0,
                        ctx.Snapshot.InteractionSurface.Observers, outbox);
                    break;

                // ── Micro-negative (criticism, ignoring, cold response…) ─────────────────
                case MicroNegative mn:
                    Upsert(self, mn.B, e => e with
                    {
                        Like = Bump(e.Like, -2.5),
                        Trust = Bump(e.Trust, -2.0),
                        Comfort = Bump(e.Comfort, -2.0),
                        TransgressionResidue = Math.Min(100, e.TransgressionResidue + Config.TransgressionMicroNegativeGain)
                    },
                    eventType: nameof(MicroNegative),
                    outcome: "negative",
                    detail: mn.Kind ?? "n/a",
                    now: mn.OccurredAt);
                    // R1 — Third-party gossip: observers update their edge toward the actor (mn.B)
                    EmitThirdPartyEvents(mn.OccurredAt, self, mn.B, mn.A,
                        ThirdPartyObservationType.NegativeAct, valence: -1.0,
                        ctx.Snapshot.InteractionSurface.Observers, outbox);
                    break;

                // ── Repair attempt ───────────────────────────────────────────────────────
                case RepairAttempt ra:
                    {
                        // Attachment.Avoidance modulates repair effectiveness.
                        // Dismissing characters (high Avoidance) treat repair attempts as intrusion
                        // → only 40% of RepairGain applied at full Avoidance.
                        var avoidance = ctx.Personality.Attachment.Avoidance;
                        var repairGainModifier = 1.0 - avoidance * 0.6;

                        Upsert(self, ra.B, e =>
                        {
                            // R2 — Contempt ceiling: RepairAttempt cannot rebuild above 30/20 once
                            // IsContemptuouslyDestroyed is set (Gottman 1994: contempt is irreversible).
                            const double ContemptTrustCeiling    = 30.0;
                            const double ContemptClosenessCeiling = 20.0;

                            var trustGain = ra.Accepted
                                ? +Config.RepairGain * repairGainModifier
                                : -Config.RupturePenalty;

                            var closenessGain = ra.Accepted
                                ? +Config.RepairGain * 0.5 * repairGainModifier
                                : -Config.RupturePenalty * 0.4;

                            var newTrust = Bump(e.Trust, trustGain);
                            var newCloseness = Bump(e.Closeness, closenessGain);

                            if (e.IsContemptuouslyDestroyed && ra.Accepted)
                            {
                                newTrust    = Math.Min(newTrust,    ContemptTrustCeiling);
                                newCloseness = Math.Min(newCloseness, ContemptClosenessCeiling);
                            }

                            return e with
                            {
                                Trust     = newTrust,
                                Closeness = newCloseness,
                                TransgressionResidue = ra.Accepted
                                    ? Math.Max(0, e.TransgressionResidue - Config.RepairGain)
                                    : Math.Min(100, e.TransgressionResidue + Config.RupturePenalty * 0.5)
                            };
                        },
                        eventType: nameof(RepairAttempt),
                        outcome: ra.Accepted ? "accepted" : "rejected",
                        detail: $"source={ra.A.Value}->{ra.B.Value}",
                        now: ra.OccurredAt);
                        break;
                    }

                // ── Interaction outcome — ACCEPTED ───────────────────────────────────────
                case InteractionOutcome io when io.Accepted:
                    {
                        var otherId = io.From == self ? io.To : io.From;
                        var otherBiology = ResolveOtherBiology(self, io.From, io.FromBiology, io.To, io.ToBiology);

                        // Trust grows only through vulnerability and acknowledgement
                        var trustDelta = io.Act switch
                        {
                            SpeechAct.SelfDisclosure => +2.5,   // sharing something personal → large trust jump
                            SpeechAct.Validation => +1.5,   // feeling understood → medium jump
                            SpeechAct.Meta => +0.5,   // comment about the relationship — slightly vulnerable
                            _ => 0.0    // SmallTalk, Humor etc. do not build trust
                        };

                        // Respect grows when the other person treats us as worthy of attention.
                        // Validation = they acknowledge our worth; Meta = they take the relationship
                        // seriously; Question = genuine curiosity signals regard.
                        var respectDelta = io.Act switch
                        {
                            SpeechAct.Validation => +1.5,   // acknowledging the other person's worth
                            SpeechAct.Meta => +1.0,   // reflecting on the relationship shows regard
                            SpeechAct.Question => +0.5,   // genuine curiosity signals respect
                            _ => 0.0
                        };

                        Upsert(self, otherId, e =>
                        {
                            // Mere-exposure: incremental familiarity boost from repeated accepted contact.
                            // Delta = total boost at (N+1) minus total boost at N.
                            // Logarithmic growth → fast early, levels off near saturation.
                            var newCount = e.PositiveInteractionCount + 1;
                            var familiarityDelta = ComputeFamiliarityExposureDelta(e.PositiveInteractionCount, newCount);
                            var stabilization = ComputeRelationalStabilization(e, ctx.PsychologyProfile);
                            var trustConsolidation = trustDelta > 0
                                ? stabilization * 0.28
                                : stabilization * 0.10;
                            var attractionPlasticity = ComputeAttractionPlasticity(e, positive: true, act: io.Act);

                            // Attachment.Avoidance caps how close A can get to B.
                            // Dismissing characters (high Avoidance) resist deep closeness.
                            var closenessGain = +1.5 + stabilization * 0.35;
                            var closenessMax = 100.0 - ctx.Personality.Attachment.Avoidance * Config.ClosenessAvoidanceCap;

                            // R3 — ExchangeStrength: Meta SpeechAct = explicit negotiation about
                            // the relationship's terms → builds transactional equity norm.
                            var exchangeGain = io.Act == SpeechAct.Meta ? 0.5 : 0.0;

                            return e with
                            {
                                Closeness = Math.Min(closenessMax, Bump(e.Closeness, closenessGain)),
                                Like = Bump(e.Like, +0.5 + stabilization * 0.20),
                                Comfort = Bump(e.Comfort, +0.8 + stabilization * 0.45 + (io.Act == SpeechAct.Invite ? SociosexualityBehaviorMath.ComfortInviteDelta(ctx.Personality.Sociosexuality) : 0.0)),
                                RomanticInterest = Bump(e.RomanticInterest, ComputeRomanticInterestDelta(e, io.Act, ctx.Personality.Sociosexuality, ctx.AttractionProfile, otherBiology ?? e.TargetBiology)),
                                SexualInterest = Bump(e.SexualInterest, ComputeSexualInterestDelta(e, io.Act, ctx.Personality.Sociosexuality, ctx.AttractionProfile, otherBiology ?? e.TargetBiology)),
                                Trust = Bump(e.Trust, Math.Max(0.0, trustDelta) + trustConsolidation),
                                Respect = respectDelta > 0 ? Bump(e.Respect, respectDelta) : e.Respect,
                                Familiarity = Bump(e.Familiarity, familiarityDelta),
                                AestheticAttraction = Bump(e.AestheticAttraction, attractionPlasticity),
                                PhysicalAttraction = Bump(e.PhysicalAttraction, attractionPlasticity * 0.65),
                                PositiveInteractionCount = newCount,
                                TargetBiology = otherBiology ?? e.TargetBiology,
                                ExchangeStrength = exchangeGain > 0
                                    ? Math.Min(100, e.ExchangeStrength + exchangeGain)
                                    : e.ExchangeStrength,
                                // SelfDisclosure grants prestige to the discloser in the recipient's eyes
                                // (emotional/intellectual courage is admirable — Cheng et al. 2013)
                                PerceivedPrestige = io.Act == SpeechAct.SelfDisclosure && io.To == self
                                    ? Clamp(e.PerceivedPrestige + Config.PrestigeGainPerSelfDisclosure)
                                    : e.PerceivedPrestige,
                                Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: true)
                            };
                        },
                        eventType: nameof(InteractionOutcome),
                        outcome: "accepted",
                        detail: $"act={io.Act}, self={(io.From == self ? "initiator" : "recipient")}, from={io.From.Value}, to={io.To.Value}",
                        now: io.OccurredAt);

                        // Žárlivost: pozorovatelé svědkové intimního aktu dostávají IntimateAct signal.
                        if (io.Act == SpeechAct.Invite && ctx.Snapshot.InteractionSurface.Observers is { Count: > 0 })
                        {
                            EmitThirdPartyEvents(io.OccurredAt, self, io.From, io.To,
                                ThirdPartyObservationType.IntimateAct, valence: 0.0,
                                ctx.Snapshot.InteractionSurface.Observers, outbox);
                        }
                        break;
                    }

                // ── Interaction outcome — REJECTED ───────────────────────────────────────
                case InteractionOutcome io when !io.Accepted:
                    {
                        var otherId = io.From == self ? io.To : io.From;
                        var otherBiology = ResolveOtherBiology(self, io.From, io.FromBiology, io.To, io.ToBiology);

                        // Rejected SelfDisclosure hurts more — vulnerability was turned away
                        var trustPenalty = io.Act switch
                        {
                            SpeechAct.SelfDisclosure => -1.5,
                            SpeechAct.Validation => -0.5,
                            _ => 0.0
                        };

                        if (io.To == self) // I rejected
                        {
                            Upsert(self, otherId, e => e with
                            {
                                Comfort = Bump(e.Comfort, -0.5),
                                Trust = trustPenalty < 0 ? Bump(e.Trust, trustPenalty) : e.Trust,
                                RomanticInterest = Bump(e.RomanticInterest, -0.5),
                                AestheticAttraction = Bump(e.AestheticAttraction, ComputeAttractionPlasticity(e, positive: false, act: io.Act) * 0.50),
                                PhysicalAttraction = Bump(e.PhysicalAttraction, ComputeAttractionPlasticity(e, positive: false, act: io.Act) * 0.35),
                                TargetBiology = otherBiology ?? e.TargetBiology,
                                Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: false)
                            },
                            eventType: nameof(InteractionOutcome),
                            outcome: "rejected",
                            detail: $"act={io.Act}, self=recipient, from={io.From.Value}, to={io.To.Value}",
                            now: io.OccurredAt);
                        }
                        else // I was rejected
                        {
                            Upsert(self, otherId, e =>
                            {
                                var stingMultiplier = ComputeRejectionStingMultiplier(e, ctx.PsychologyProfile, ctx.Personality.Attachment);

                                // TransgressionResidue for intimate rejection (InviteIntimacy).
                                var residueDelta = io.Act == SpeechAct.Invite
                                    ? Config.TransgressionRejectionGain
                                    : 0.0;

                                return e with
                                {
                                    Like = Bump(e.Like, -1.5 * stingMultiplier),
                                    Comfort = Bump(e.Comfort, -2.0 * stingMultiplier),
                                    Trust = trustPenalty < 0 ? Bump(e.Trust, trustPenalty * stingMultiplier) : e.Trust,
                                    RomanticInterest = Bump(e.RomanticInterest, (io.Act == SpeechAct.Invite ? -2.0 : -1.0) * stingMultiplier),
                                    AestheticAttraction = Bump(e.AestheticAttraction, ComputeAttractionPlasticity(e, positive: false, act: io.Act)),
                                    PhysicalAttraction = Bump(e.PhysicalAttraction, ComputeAttractionPlasticity(e, positive: false, act: io.Act) * 0.65),
                                    TargetBiology = otherBiology ?? e.TargetBiology,
                                    TransgressionResidue = Math.Min(100, e.TransgressionResidue + residueDelta),
                                    Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: false)
                                };
                            },
                            eventType: nameof(InteractionOutcome),
                            outcome: "rejected",
                            detail: $"act={io.Act}, self=initiator, from={io.From.Value}, to={io.To.Value}",
                            now: io.OccurredAt);

                            // Williams' Temporal Need-Threat Model (Hartgerink et al. 2015, k=120, d > |1.4|):
                            // Intimate rejection threatens 4 needs simultaneously. Publish cross-engine event.
                            if (io.Act == SpeechAct.Invite)
                            {
                                if (State.Edges.TryGetValue(otherId, out var rejEdge))
                                {
                                    var sting = ComputeRejectionStingMultiplier(rejEdge, ctx.PsychologyProfile, ctx.Personality.Attachment);
                                    outbox.Add(new RejectionNeedsThreat(io.OccurredAt, self, sting, IsIntimateAdvance: true));
                                }
                            }
                        }

                        break;
                    }

                // ── Touch outcome ────────────────────────────────────────────────────────
                case TouchOutcome to:
                    {
                        var otherId = to.From == self ? to.To : to.From;

                        if (to.To == self) // I was touched — I decided
                        {
                            if (to.Accepted)
                            {
                                Upsert(self, otherId, e => e with
                                {
                                    Comfort = Bump(e.Comfort, +1.5),
                                    Closeness = Bump(e.Closeness, +1.0),
                                    SexualInterest = Bump(e.SexualInterest, to.Level == TouchLevel.Intimate ? +4.0 : to.Level == TouchLevel.Friendly ? +1.5 : +0.4),
                                    RomanticInterest = to.Level == TouchLevel.Intimate && e.Trust >= 60 && e.Comfort >= 60
                                        ? Bump(e.RomanticInterest, +1.5)
                                        : e.RomanticInterest,
                                    // Intimate touch builds communal relationship norms (Clark & Mills 2012)
                                    CommunalStrength = to.Level == TouchLevel.Intimate
                                        ? Math.Min(100, e.CommunalStrength + Config.CommunalGrowthPerIntimateInteraction)
                                        : e.CommunalStrength,
                                    Breakdown = e.Breakdown with
                                    {
                                        Physical = BumpD(e.Breakdown.Physical, TouchBoost(to.Level)),
                                        // Intimate touch signals aesthetic appreciation of the other person.
                                        // Light/Friendly touches build the Physical domain only — intimacy
                                        // is the threshold at which Aesthetics becomes relevant.
                                        Aesthetics = to.Level == TouchLevel.Intimate
                                            ? BumpD(e.Breakdown.Aesthetics, +3.0)
                                            : e.Breakdown.Aesthetics
                                    }
                                },
                                eventType: nameof(TouchOutcome),
                                outcome: "accepted",
                                detail: $"level={to.Level}, self=recipient, from={to.From.Value}, to={to.To.Value}",
                                now: to.OccurredAt);
                            }
                            else
                            {
                                // I rejected — mild discomfort
                                Upsert(self, otherId, e => e with
                                {
                                    Comfort = Bump(e.Comfort, -1.0),
                                    RomanticInterest = Bump(e.RomanticInterest, to.Level == TouchLevel.Intimate ? -1.5 : -0.5)
                                },
                                eventType: nameof(TouchOutcome),
                                outcome: "rejected",
                                detail: $"level={to.Level}, self=recipient, from={to.From.Value}, to={to.To.Value}",
                                now: to.OccurredAt);
                            }
                        }
                        else // I was rejected
                        {
                            Upsert(self, otherId, e => e with
                            {
                                Like = Bump(e.Like, to.Accepted ? +0.5 : -2.0),
                                Comfort = Bump(e.Comfort, to.Accepted ? +1.0 : -3.0),
                                SexualInterest = Bump(e.SexualInterest, to.Accepted
                                    ? (to.Level == TouchLevel.Intimate ? +2.5 : +0.5)
                                    : (to.Level == TouchLevel.Intimate ? -2.0 : -0.5))
                            },
                            eventType: nameof(TouchOutcome),
                            outcome: to.Accepted ? "accepted" : "rejected",
                            detail: $"level={to.Level}, self=initiator, from={to.From.Value}, to={to.To.Value}",
                            now: to.OccurredAt);
                        }

                        break;
                    }

                // ── Third-party observation — R1 gossip handler ──────────────────────────
                // Also updates perceived Dominance/Prestige (Cheng et al. 2013):
                //   PositiveAct → Prestige ↑ (admirable, copy-worthy behavior)
                //   NegativeAct/Betrayal → Dominance ↑ (coercive, threatening)
                case ThirdPartyActionObserved tpa when tpa.Observer == self:
                    {
                        // Observer updates their edge toward the Actor.
                        // Indirect evidence is weaker than direct interaction (~30–50%).
                        // Feinberg et al. 2014: gossip deters selfishness and shapes reputation.
                        var gossipScale = tpa.Type == ThirdPartyObservationType.Betrayal ? 0.5 : 0.3;
                        Upsert(self, tpa.Actor, e => tpa.Type switch
                        {
                            ThirdPartyObservationType.PositiveAct => e with
                            {
                                Like  = Bump(e.Like,  +2.0 * gossipScale),
                                Trust = Bump(e.Trust, +1.5 * gossipScale),
                                PerceivedPrestige = Clamp(e.PerceivedPrestige + Config.PrestigeGainPerPositiveAct)
                            },
                            ThirdPartyObservationType.NegativeAct => e with
                            {
                                Like  = Bump(e.Like,  -2.5 * gossipScale),
                                Trust = Bump(e.Trust, -2.0 * gossipScale),
                                PerceivedDominance = Clamp(e.PerceivedDominance + Config.DominanceGainPerNegativeAct)
                            },
                            // Betrayal: step-drop (Slovic 1993; skill ref: 30–70 % of Trust)
                            ThirdPartyObservationType.Betrayal => e with
                            {
                                Like  = Bump(e.Like,  -20.0 * gossipScale),
                                Trust = Bump(e.Trust, -30.0 * gossipScale),
                                PerceivedDominance = Clamp(e.PerceivedDominance + Config.DominanceGainPerNegativeAct * 2.0)
                            },
                            _ => e
                        },
                        eventType: nameof(ThirdPartyActionObserved),
                        outcome: tpa.Type.ToString().ToLowerInvariant(),
                        detail: $"actor={tpa.Actor.Value}, target={tpa.Target.Value}, valence={tpa.Valence:F1}",
                        now: tpa.OccurredAt);

                        // Žárlivost — IntimateAct u osoby, k níž má pozorovatel romantický/sexuální zájem.
                        // Robustní sex difference ve forced-choice formátu (Buss et al. 1992, 48 zemí).
                        // Distribuce se překrývají → individální variance přes AttachmentAnxiety a SOI.
                        if (tpa.Type == ThirdPartyObservationType.IntimateAct
                            && State.Edges.TryGetValue(tpa.Actor, out var actorEdge))
                        {
                            var intimacyInterest = actorEdge.RomanticInterest * 0.55 + actorEdge.SexualInterest * 0.45;
                            if (intimacyInterest >= 25.0)
                            {
                                var anxiety = ctx.Personality.Attachment.Anxiety;
                                var soiAttitude = ctx.Personality.Sociosexuality.Attitude;
                                // Muži: mírně vyšší sexuální žárlivost (forced-choice d ~ 0,3; slabé v Likert)
                                var sexualBias = ctx.Biology == SexBiology.Male ? 1.2 : 1.0;
                                var jealousyBase = Math.Clamp(intimacyInterest / 100.0, 0, 1);
                                var jealousyIntensity = jealousyBase * sexualBias
                                                      * (1.0 + anxiety * 0.8)
                                                      * (1.0 - soiAttitude * 0.3);
                                var transgressionAmount = Math.Clamp(jealousyIntensity * 12.0, 0, 20.0);
                                Upsert(self, tpa.Actor, e => e with
                                {
                                    TransgressionResidue = Math.Min(100, e.TransgressionResidue + transgressionAmount)
                                },
                                eventType: "JealousyDistress",
                                outcome: $"distress={transgressionAmount:F1}",
                                detail: $"actor={tpa.Actor.Value}, intimacyInterest={intimacyInterest:F0}");
                            }
                        }
                        break;
                    }

                // ── Contempt — terminal relationship marker (Gottman 1994) ────────────────
                case ContemptuousActPerformed ca when (ca.From == self || ca.To == self):
                    {
                        // Contempt directed FROM self toward someone: self's edge to that person.
                        // Contempt received BY self from someone: self's edge to that person.
                        var contemptTarget = ca.From == self ? ca.To : ca.From;
                        Upsert(self, contemptTarget, e => e with
                        {
                            // Irreversible step-drop in Trust and Like
                            Trust = Bump(e.Trust, -35.0),
                            Like  = Bump(e.Like,  -30.0),
                            Comfort = Bump(e.Comfort, -20.0),
                            // Flag is permanent — cannot be cleared by repair
                            IsContemptuouslyDestroyed = true,
                            // Contempt signals coercive intent — raises perceived dominance of the expresser
                            PerceivedDominance = Clamp(e.PerceivedDominance + Config.DominanceGainPerContempt)
                        },
                        eventType: nameof(ContemptuousActPerformed),
                        outcome: ca.From == self ? "expressed" : "received",
                        detail: $"from={ca.From.Value}, to={ca.To.Value}",
                        now: ca.OccurredAt);
                        break;
                    }

                case SexualEncounterOutcome se:
                    {
                        var otherId = se.From == self ? se.To : se.From;
                        var otherBiology = ResolveOtherBiology(self, se.From, se.FromBiology, se.To, se.ToBiology);

                        if (se.Accepted)
                        {
                            Upsert(self, otherId, e => e with
                            {
                                Comfort = Bump(e.Comfort, +1.2 + SociosexualityBehaviorMath.ComfortInviteDelta(ctx.Personality.Sociosexuality)),
                                Closeness = Bump(e.Closeness, +2.0),
                                RomanticInterest = Bump(e.RomanticInterest, 0.8 * SociosexualityBehaviorMath.RomanticInviteDeltaMultiplier(ctx.Personality.Sociosexuality) * SexualOrientationBehaviorMath.RomanticInterestMultiplier(ctx.AttractionProfile, otherBiology ?? e.TargetBiology)),
                                SexualInterest = Bump(e.SexualInterest, 2.4 * SociosexualityBehaviorMath.SexualInterestDeltaMultiplier(ctx.Personality.Sociosexuality) * SexualOrientationBehaviorMath.SexualInterestMultiplier(ctx.AttractionProfile, otherBiology ?? e.TargetBiology)),
                                TargetBiology = otherBiology ?? e.TargetBiology,
                                // Sexual encounter deepens communal norms (Clark & Mills 2012)
                                CommunalStrength = Math.Min(100, e.CommunalStrength + Config.CommunalGrowthPerIntimateInteraction),
                                Breakdown = e.Breakdown with
                                {
                                    Physical = BumpD(e.Breakdown.Physical, +3.0)
                                }
                            },
                            eventType: nameof(SexualEncounterOutcome),
                            outcome: "accepted",
                            detail: $"self={(se.From == self ? "initiator" : "recipient")}, reproductive={se.ReproductivePotential}",
                            now: se.OccurredAt);
                        }
                        else
                        {
                            Upsert(self, otherId, e => e with
                            {
                                Comfort = Bump(e.Comfort, -2.0),
                                Trust = Bump(e.Trust, -0.8),
                                RomanticInterest = Bump(e.RomanticInterest, -1.2),
                                SexualInterest = Bump(e.SexualInterest, se.From == self ? -2.4 : -0.8),
                                TargetBiology = otherBiology ?? e.TargetBiology
                            },
                            eventType: nameof(SexualEncounterOutcome),
                            outcome: "declined",
                            detail: $"self={(se.From == self ? "initiator" : "recipient")}",
                            now: se.OccurredAt);
                        }

                        break;
                    }
            }
        }

        #endregion Handle — domain event processing

        #region Tick — time decay

        /// <inheritdoc/>
        /// <remarks>
        /// Without regular contact, relationships drift toward neutral values.
        /// Each dimension decays at a different rate:
        /// <list type="bullet">
        ///   <item>Closeness — fastest decay (distance is felt quickly)</item>
        ///   <item>Trust — slowest decay (trust earned persists)</item>
        ///   <item>Familiarity — very slow decay (repeated exposure leaves a long memory)</item>
        ///   <item>RomanticInterest / SexualInterest — drift back toward soft neutral when not reinforced</item>
        /// </list>
        /// Note: <c>PositiveInteractionCount</c> does <b>not</b> decay — it is a cumulative
        /// historical counter, not a relationship intensity measure.
        /// </remarks>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0, dt.TotalDays);
            if (days == 0 || State.Edges.Count == 0)
            {
                return;
            }

            var fidelity = _socialFidelityPolicy.GetLevel(ctx.Id);
            _deferredDecayDays += days;

            if (!ShouldApplySocialDecay(_deferredDecayDays, fidelity))
            {
                return;
            }

            days = _deferredDecayDays;
            _deferredDecayDays = 0;

            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);
            var psych = ctx.Snapshot.Psychology;

            // Psychology modulates decay: positive mood slightly improves, stress damages
            var valenceEffect = psych.Valence * 0.3 * days;
            var stressEffect = psych.Stress * 0.02 * days;

            // ── Dunbar finite attention budget (Saramaki et al. 2014, PNAS) ──────────────
            // Total social-maintenance effort is roughly fixed. When the number of intimate
            // (Tier-1) or close-friend (Tier-2) ties exceeds the soft capacity, remaining
            // bandwidth per lower-tier edge shrinks → decay rate increases proportionally.
            var tier1Count = State.Edges.Values.Count(e => e.Closeness >= Config.DunbarTier1Threshold);
            var tier2Count = State.Edges.Values.Count(e => e.Closeness >= Config.DunbarTier2Threshold
                                                         && e.Closeness <  Config.DunbarTier1Threshold);
            var tier1Excess = Math.Max(0, tier1Count - Config.DunbarTier1Capacity);
            var tier2Excess = Math.Max(0, tier2Count - Config.DunbarTier2Capacity);

            foreach (var kv in State.Edges)
            {
                var e = kv.Value;

                // Established relationships decay slower: accumulated history provides inertia.
                // At PositiveInteractionCount = MereExposureSaturation (20), decay is ~60% of base rate.
                // Minimum decay factor 0.40 — even deep bonds fade with sustained absence.
                var depthFactor = Math.Clamp(
                    1.0 - Math.Log(1.0 + e.PositiveInteractionCount)
                              / Math.Log(1.0 + Config.MereExposureSaturation) * 0.60,
                    0.40,
                    1.0);

                // ── Navarro 8× gap rule (Navarro et al. 2017) ────────────────────────────
                // If the gap since last contact exceeds 8× the expected contact interval,
                // tie-death hazard spikes. Apply an accelerated decay multiplier.
                var navarrMultiplier = 1.0;
                if (e.LastContactTime.HasValue)
                {
                    var gapDays = WDateTime.Difference(now, e.LastContactTime.Value).TotalDays;
                    if (gapDays > 8.0 * Config.ExpectedContactIntervalDays)
                    {
                        navarrMultiplier = Config.NavarrGapMultiplier;
                    }
                }

                var d = Config.DecayPerDay * days * depthFactor * navarrMultiplier;

                // Dunbar attention pressure: each excess Tier-1 tie steals maintenance bandwidth
                // from lower-tier edges (Tier-2 and below). Each excess Tier-2 additionally
                // pressures Tier-3/4. Tier-1 edges themselves are never penalised — they are
                // the ones consuming the budget. (Miritello et al. 2013: finite attention budget.)
                if (e.Closeness < Config.DunbarTier1Threshold && (tier1Excess > 0 || tier2Excess > 0))
                {
                    var pressure = tier1Excess * Config.AttentionBudgetPressurePerExcessTier1;
                    if (e.Closeness < Config.DunbarTier2Threshold)
                        pressure += tier2Excess * Config.AttentionBudgetPressurePerExcessTier2;
                    d *= 1.0 + pressure;
                }

                // Domain breakdown decays very slowly toward neutral (50) without reinforcement.
                // Rate is 10× slower than Closeness — domains are more stable impressions than
                // moment-to-moment feelings, but they do fade without continued confirmation.
                // PositiveInteractionCount does not decay — it is a cumulative historical counter.
                var dd = d * 0.1;

                // ── TransgressionResidue power-law decay ─────────────────────────────────
                // residue *= (1 - rate)^days; prune to 0 below 0.5.
                var newResidue = e.TransgressionResidue;
                if (newResidue > 0)
                {
                    newResidue *= Math.Pow(1.0 - Config.TransgressionDecayRatePerDay, days);
                    if (newResidue < 0.5)
                    {
                        newResidue = 0;
                    }
                }

                // ── Familiarity–Like dissonance (non-monotonicity) ───────────────────────
                // Norton, Frost & Ariely 2007: high Familiarity without ongoing contact
                // erodes Like (overexposure without renewal breeds indifference).
                var familiarityLikePenalty = 0.0;
                if (e.Familiarity > 55 && e.LastContactTime.HasValue)
                {
                    var daysSinceContact = WDateTime.Difference(now, e.LastContactTime.Value).TotalDays;
                    if (daysSinceContact > 30)
                    {
                        familiarityLikePenalty = Config.FamiliarityLikeDissonancePenalty * days;
                    }
                }

                // S2 — Basson 2001: ResponsiveDesireLevel grows slowly in long-term communal
                // relationships. Once CommunalStrength > 60 AND PositiveInteractionCount > 30,
                // the NPC gradually shifts from spontaneous initiator to responsive acceptor.
                var responsiveTarget = 0.0;
                if (e.CommunalStrength > 60 && e.PositiveInteractionCount > 30)
                {
                    // Target scales with CommunalStrength beyond threshold; saturates at 80.
                    responsiveTarget = Math.Min(80.0, (e.CommunalStrength - 60.0) / 40.0 * 80.0);
                }
                // Drift rate: ~1 pt/day toward target (slow shift over months/years)
                var newResponsive = Clamp(Approach(e.ResponsiveDesireLevel, responsiveTarget, 1.0 * days));

                // ── Tonic / phasic sexual interest decay (Baumeister et al. 2001) ────────────
                // When SexualInterest is below the tonic threshold it represents stable physical
                // desire rooted in attractiveness — it should persist for months without contact.
                // When it is above the threshold it represents active phasic passion, which fades
                // quickly due to habituation (Coolidge effect). Full 1.50× rate applies there.
                var effectiveSexualInterestDecayMult = e.SexualInterest < Config.TonicSexualInterestThreshold
                    ? Config.DecayMultiplierSexualInterest * Config.TonicSexualInterestDecayFactor
                    : Config.DecayMultiplierSexualInterest;

                dict[kv.Key] = e with
                {
                    Like = Clamp(Approach(e.Like, 50, d * Config.DecayMultiplierLike) + valenceEffect - stressEffect - familiarityLikePenalty),
                    Trust = Clamp(Approach(e.Trust, 50, d * Config.DecayMultiplierTrust)),
                    Familiarity = Clamp(Approach(e.Familiarity, Config.FamiliarityDecayFloor, d * Config.DecayMultiplierFamiliarity)),
                    AestheticAttraction = e.AestheticAttraction,
                    PhysicalAttraction = e.PhysicalAttraction,
                    RomanticInterest = Clamp(Approach(e.RomanticInterest, 5, d * Config.DecayMultiplierRomanticInterest)),
                    SexualInterest = Clamp(Approach(e.SexualInterest, 5, d * effectiveSexualInterestDecayMult)),
                    Closeness = Clamp(Approach(e.Closeness, 5, d * Config.DecayMultiplierCloseness)),
                    Respect = Clamp(Approach(e.Respect, 55, d * Config.DecayMultiplierRespect)),
                    PerceivedDominance = Clamp(Approach(e.PerceivedDominance, 50, d * Config.DecayMultiplierDominance)),
                    PerceivedPrestige  = Clamp(Approach(e.PerceivedPrestige,  50, d * Config.DecayMultiplierPrestige)),
                    Comfort = Clamp(Approach(e.Comfort, 45, d * Config.DecayMultiplierComfort) + valenceEffect * 0.5 - stressEffect * 0.5),
                    TransgressionResidue = newResidue,
                    ResponsiveDesireLevel = newResponsive,
                    Breakdown = new DomainBreakdown(
                        Intellect: Clamp(Approach(e.Breakdown.Intellect, 50, dd)),
                        Humor: Clamp(Approach(e.Breakdown.Humor, 50, dd)),
                        // Physical and Aesthetics do not decay — they are stable perceptual
                        // impressions anchored in appearance, not in remembered interactions.
                        // You do not forget whether someone was physically attractive.
                        Aesthetics: e.Breakdown.Aesthetics,
                        Values: Clamp(Approach(e.Breakdown.Values, 50, dd)),
                        Physical: e.Breakdown.Physical
                    )
                };
            }

            State = new RelationshipState(dict);

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultRelationshipsEngine)))
            {
                _log.RelDecayApplied(ctx.Id.Value.ToString(), State.Edges.Count, days);
            }
        }

        #endregion Tick — time decay

        #region RestoreState

        /// <inheritdoc/>
        public void RestoreState(RelationshipState state) => State = state;

        #endregion RestoreState
    }
}
