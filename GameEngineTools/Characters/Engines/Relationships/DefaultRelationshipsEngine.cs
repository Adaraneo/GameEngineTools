// DefaultRelationshipsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
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

        #endregion Private fields

        #region Constructor

        /// <summary>Creates an engine instance with an empty relationship graph.</summary>
        public DefaultRelationshipsEngine(IOptions<RelationshipsConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultRelationshipsEngine>();
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
                    var other = self == fi.A ? fi.B : self == fi.B ? fi.A : fi.B;
                    if (other == self)
                    {
                        break;
                    }

                    Upsert(self, other, e => e with
                    {
                        // Lerp 70% toward the first impression — does not fully override
                        // if the character already knows the person slightly.
                        Like = Lerp(e.Like, fi.Like, 0.7),
                        Familiarity = Math.Max(e.Familiarity, 2),
                        Trust = e.Trust <= 0 ? 50 : e.Trust,
                        Closeness = Math.Max(e.Closeness, 0),
                        AestheticAttraction = Lerp(e.AestheticAttraction, fi.PreferenceMatch / 35.0 * 100.0, 0.8),
                        PhysicalAttraction = Lerp(e.PhysicalAttraction, fi.BasePhysical / 40.0 * 100.0, 0.8),
                        RomanticInterest = e.RomanticInterest,
                        SexualInterest = e.SexualInterest,
                        Breakdown = e.Breakdown with
                        {
                            // BasePhysical (max 40) → normalised to [0, 100] for the Physical domain.
                            // Models the immediate "this person is physically striking" impression
                            // from evolutionary signals: WHR, height range, facial symmetry.
                            Physical = Lerp(e.Breakdown.Physical,
                                             fi.BasePhysical / 40.0 * 100.0, 0.8),

                            // PreferenceMatch (max 35) → normalised to [0, 100] for the Aesthetics domain.
                            // Models personal taste — how closely the target matches the observer's
                            // own physical ideal (height preference, frame, WHR preference).
                            Aesthetics = Lerp(e.Breakdown.Aesthetics,
                                             fi.PreferenceMatch / 35.0 * 100.0, 0.8)
                        }
                    },
                    eventType: nameof(FirstImpressionFormed),
                    outcome: "formed",
                    detail: $"source={fi.A.Value}, like={fi.Like:F1}, basePhysical={fi.BasePhysical:F1}, preferenceMatch={fi.PreferenceMatch:F1}");

                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultRelationshipsEngine))))
                    {
                        _log.RelFirstImpression(
                            ctx.Id.Value.ToString(),
                            fi.A.Value.ToString(), fi.B.Value.ToString(),
                        fi.Like);
                    }

                    break;

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
                    detail: mp.What ?? "n/a");
                    break;

                // ── Micro-negative (criticism, ignoring, cold response…) ─────────────────
                case MicroNegative mn:
                    Upsert(self, mn.B, e => e with
                    {
                        Like = Bump(e.Like, -2.5),
                        Trust = Bump(e.Trust, -2.0),
                        Comfort = Bump(e.Comfort, -2.0)
                    },
                    eventType: nameof(MicroNegative),
                    outcome: "negative",
                    detail: mn.What ?? "n/a");
                    break;

                // ── Repair attempt ───────────────────────────────────────────────────────
                case RepairAttempt ra:
                    Upsert(self, ra.B, e => e with
                    {
                        Trust = Bump(e.Trust, ra.Accepted ? +Config.RepairGain : -Config.RupturePenalty),
                        Closeness = Bump(e.Closeness, ra.Accepted ? +Config.RepairGain * 0.5 : -Config.RupturePenalty * 0.4)
                    },
                    eventType: nameof(RepairAttempt),
                    outcome: ra.Accepted ? "accepted" : "rejected",
                    detail: $"source={ra.A.Value}->{ra.B.Value}");
                    break;

                // ── Interaction outcome — ACCEPTED ───────────────────────────────────────
                case InteractionOutcome io when io.Accepted:
                    {
                        var otherId = io.From == self ? io.To : io.From;

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

                            return e with
                            {
                                Closeness = Bump(e.Closeness, +1.5),
                                Like = Bump(e.Like, +0.5),
                                Comfort = Bump(e.Comfort, +0.8),
                                RomanticInterest = Bump(e.RomanticInterest, ComputeRomanticInterestDelta(e, io.Act)),
                                SexualInterest = Bump(e.SexualInterest, ComputeSexualInterestDelta(e, io.Act)),
                                Trust = trustDelta > 0 ? Bump(e.Trust, trustDelta) : e.Trust,
                                Respect = respectDelta > 0 ? Bump(e.Respect, respectDelta) : e.Respect,
                                Familiarity = Bump(e.Familiarity, familiarityDelta),
                                PositiveInteractionCount = newCount,
                                Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: true)
                            };
                        },
                        eventType: nameof(InteractionOutcome),
                        outcome: "accepted",
                        detail: $"act={io.Act}, self={(io.From == self ? "initiator" : "recipient")}, from={io.From.Value}, to={io.To.Value}");
                        break;
                    }

                // ── Interaction outcome — REJECTED ───────────────────────────────────────
                case InteractionOutcome io when !io.Accepted:
                    {
                        var otherId = io.From == self ? io.To : io.From;

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
                                Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: false)
                            },
                            eventType: nameof(InteractionOutcome),
                            outcome: "rejected",
                            detail: $"act={io.Act}, self=recipient, from={io.From.Value}, to={io.To.Value}");
                        }
                        else // I was rejected
                        {
                            Upsert(self, otherId, e => e with
                            {
                                Like = Bump(e.Like, -1.5),
                                Comfort = Bump(e.Comfort, -2.0),
                                Trust = trustPenalty < 0 ? Bump(e.Trust, trustPenalty) : e.Trust,
                                RomanticInterest = Bump(e.RomanticInterest, io.Act == SpeechAct.Invite ? -2.0 : -1.0),
                                Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: false)
                            },
                            eventType: nameof(InteractionOutcome),
                            outcome: "rejected",
                            detail: $"act={io.Act}, self=initiator, from={io.From.Value}, to={io.To.Value}");
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
                                detail: $"level={to.Level}, self=recipient, from={to.From.Value}, to={to.To.Value}");
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
                                detail: $"level={to.Level}, self=recipient, from={to.From.Value}, to={to.To.Value}");
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
                            detail: $"level={to.Level}, self=initiator, from={to.From.Value}, to={to.To.Value}");
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

            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);
            var psych = ctx.Snapshot.Psychology;

            // Psychology modulates decay: positive mood slightly improves, stress damages
            var valenceEffect = psych.Valence * 0.3 * days;
            var stressEffect = psych.Stress * 0.02 * days;

            foreach (var kv in State.Edges)
            {
                var e = kv.Value;
                var d = Config.DecayPerDay * days;

                // Domain breakdown decays very slowly toward neutral (50) without reinforcement.
                // Rate is 10× slower than Closeness — domains are more stable impressions than
                // moment-to-moment feelings, but they do fade without continued confirmation.
                // PositiveInteractionCount does not decay — it is a cumulative historical counter.
                var dd = d * 0.1;

                dict[kv.Key] = e with
                {
                    Like = Clamp(Approach(e.Like, 50, d) + valenceEffect - stressEffect),
                    Trust = Clamp(Approach(e.Trust, 50, d * 0.5)),
                    Familiarity = Clamp(Approach(e.Familiarity, 10, d * 0.08)),
                    AestheticAttraction = e.AestheticAttraction,
                    PhysicalAttraction = e.PhysicalAttraction,
                    RomanticInterest = Clamp(Approach(e.RomanticInterest, 5, d * 0.45)),
                    SexualInterest = Clamp(Approach(e.SexualInterest, 5, d * 0.70)),
                    Closeness = Clamp(Approach(e.Closeness, 5, d * 1.2)),
                    Respect = Clamp(Approach(e.Respect, 55, d * 0.3)),
                    Comfort = Clamp(Approach(e.Comfort, 45, d * 0.6) + valenceEffect * 0.5 - stressEffect * 0.5),
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

            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultRelationshipsEngine))))
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
