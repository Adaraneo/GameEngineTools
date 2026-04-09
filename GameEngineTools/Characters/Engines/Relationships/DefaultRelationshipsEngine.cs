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
    /// A logarithmic bonus is applied to <c>Attraction</c> on every increment,
    /// saturating at <see cref="RelationshipsConfig.MereExposureSaturation"/> interactions.
    /// This keeps the effect in the engine that owns Attraction — no external calculator needed.
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
                    Upsert(self, fi.B, e => RecomputeLegacy(e with
                    {
                        // Lerp 70% toward the first impression — does not fully override
                        // if the character already knows the person slightly.
                        Like = Lerp(e.Like, fi.Like, 0.7),
                        Familiarity = Math.Max(e.Familiarity, 8),
                        Trust = e.Trust <= 0 ? 45 : e.Trust,
                        Closeness = Math.Max(e.Closeness, 10),
                        AestheticAttraction = Lerp(e.AestheticAttraction, fi.PreferenceMatch / 35.0 * 100.0, 0.7),
                        PhysicalAttraction = Lerp(e.PhysicalAttraction, fi.BasePhysical / 40.0 * 100.0, 0.7),
                        RomanticInterest = Lerp(e.RomanticInterest, Math.Max(35.0, fi.Like * 0.55), 0.15),
                        SexualInterest = Lerp(e.SexualInterest, Math.Max(30.0, fi.Attraction * 0.45), 0.10),
                        Breakdown = e.Breakdown with
                        {
                            // BasePhysical (max 40) → normalised to [0, 100] for the Physical domain.
                            // Models the immediate "this person is physically striking" impression
                            // from evolutionary signals: WHR, height range, facial symmetry.
                            Physical = Lerp(e.Breakdown.Physical,
                                             fi.BasePhysical / 40.0 * 100.0, 0.7),

                            // PreferenceMatch (max 35) → normalised to [0, 100] for the Aesthetics domain.
                            // Models personal taste — how closely the target matches the observer's
                            // own physical ideal (height preference, frame, WHR preference).
                            Aesthetics = Lerp(e.Breakdown.Aesthetics,
                                             fi.PreferenceMatch / 35.0 * 100.0, 0.7)
                        }
                    }));

                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultRelationshipsEngine))))
                    {
                        _log.RelFirstImpression(
                            ctx.Id.Value.ToString(),
                            fi.A.Value.ToString(), fi.B.Value.ToString(),
                            fi.Like, fi.Attraction);
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
                    });
                    break;

                // ── Micro-negative (criticism, ignoring, cold response…) ─────────────────
                case MicroNegative mn:
                    Upsert(self, mn.B, e => e with
                    {
                        Like = Bump(e.Like, -2.5),
                        Trust = Bump(e.Trust, -2.0),
                        Comfort = Bump(e.Comfort, -2.0)
                    });
                    break;

                // ── Repair attempt ───────────────────────────────────────────────────────
                case RepairAttempt ra:
                    Upsert(self, ra.B, e => e with
                    {
                        Trust = Bump(e.Trust, ra.Accepted ? +Config.RepairGain : -Config.RupturePenalty),
                        Closeness = Bump(e.Closeness, ra.Accepted ? +Config.RepairGain * 0.5 : -Config.RupturePenalty * 0.4)
                    });
                    break;

                // ── Interaction outcome — ACCEPTED ───────────────────────────────────────
                case InteractionOutcome io when io.Accepted:
                    {
                        var otherId = io.From == self ? io.To : io.From;
                        EnsureEdge(self, otherId);

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
                            // Mere-exposure: incremental attraction boost from familiarity.
                            // Delta = total boost at (N+1) minus total boost at N.
                            // Logarithmic growth → fast early, levels off near saturation.
                            var newCount = e.PositiveInteractionCount + 1;
                            var exposureDelta = MereExposureBoost(newCount, Config)
                                              - MereExposureBoost(e.PositiveInteractionCount, Config);

                            return e with
                            {
                                Closeness = Bump(e.Closeness, +1.5),
                                Like = Bump(e.Like, +0.5),
                                Comfort = Bump(e.Comfort, +0.8),
                                RomanticInterest = Bump(e.RomanticInterest, ComputeRomanticInterestDelta(e)),
                                SexualInterest = Bump(e.SexualInterest, ComputeSexualInterestDelta(e)),
                                Trust = trustDelta > 0 ? Bump(e.Trust, trustDelta) : e.Trust,
                                Respect = respectDelta > 0 ? Bump(e.Respect, respectDelta) : e.Respect,
                                Familiarity = Bump(e.Familiarity, exposureDelta),
                                PositiveInteractionCount = newCount,
                                Breakdown = ApplyDomainBoost(e.Breakdown, io.Act, accepted: true)
                            };
                        });
                        break;
                    }

                // ── Interaction outcome — REJECTED ───────────────────────────────────────
                case InteractionOutcome io when !io.Accepted:
                    {
                        var otherId = io.From == self ? io.To : io.From;
                        EnsureEdge(self, otherId);

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
                            });
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
                            });
                        }

                        break;
                    }

                // ── Touch outcome ────────────────────────────────────────────────────────
                case TouchOutcome to:
                    {
                        var otherId = to.From == self ? to.To : to.From;
                        EnsureEdge(self, otherId);

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
                                });
                            }
                            else
                            {
                                // I rejected — mild discomfort
                                Upsert(self, otherId, e => e with
                                {
                                    Comfort = Bump(e.Comfort, -1.0),
                                    RomanticInterest = Bump(e.RomanticInterest, to.Level == TouchLevel.Intimate ? -1.5 : -0.5)
                                });
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
                            });
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
        ///   <item>Attraction — slow, drifts toward 35 or 45 based on initial value</item>
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
                    Familiarity = Clamp(Approach(e.Familiarity, 25, d * 0.08)),
                    AestheticAttraction = e.AestheticAttraction,
                    PhysicalAttraction = e.PhysicalAttraction,
                    RomanticInterest = Clamp(Approach(e.RomanticInterest, 35, d * 0.45)),
                    SexualInterest = Clamp(Approach(e.SexualInterest, 30, d * 0.70)),
                    Closeness = Clamp(Approach(e.Closeness, 35, d * 1.2)),
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

#if false

        #region Private methods — DomainBreakdown

        /// <summary>
        /// Updates <see cref="DomainBreakdown"/> based on the type of speech act.
        /// </summary>
        /// <remarks>
        /// Each <see cref="SpeechAct"/> affects different domains:
        /// <list type="table">
        ///   <item><term>SmallTalk</term><description>Humor +1.5</description></item>
        ///   <item><term>Question</term><description>Intellect +2.0</description></item>
        ///   <item><term>SelfDisclosure</term><description>Values +2.0</description></item>
        ///   <item><term>Validation</term><description>Values +1.0</description></item>
        ///   <item><term>Humor</term><description>Humor +2.5</description></item>
        ///   <item><term>Meta</term><description>Intellect +1.0</description></item>
        ///   <item><term>Invite</term><description>Physical +0.5</description></item>
        ///   <item><term>Boundary</term><description>Values −1.0</description></item>
        /// </list>
        /// Rejected interactions have half the effect — the event is registered but weaker.
        /// </remarks>
        private static DomainBreakdown ApplyDomainBoost(DomainBreakdown bd, SpeechAct act, bool accepted)
        {
            var mul = accepted ? 1.0 : 0.5;

            return act switch
            {
                SpeechAct.SmallTalk      => bd with { Humor     = BumpD(bd.Humor,     +1.5 * mul) },
                SpeechAct.Question       => bd with { Intellect = BumpD(bd.Intellect, +2.0 * mul) },
                SpeechAct.SelfDisclosure => bd with { Values    = BumpD(bd.Values,    +2.0 * mul) },
                SpeechAct.Validation     => bd with { Values    = BumpD(bd.Values,    +1.0 * mul) },
                SpeechAct.Humor          => bd with { Humor     = BumpD(bd.Humor,     +2.5 * mul) },
                SpeechAct.Meta           => bd with { Intellect = BumpD(bd.Intellect, +1.0 * mul) },
                SpeechAct.Invite         => bd with { Physical  = BumpD(bd.Physical,  +0.5 * mul) },
                SpeechAct.Boundary       => bd with { Values    = BumpD(bd.Values,    -1.0 * mul) },
                _                        => bd
            };
        }

        #endregion Private methods — DomainBreakdown

        #region Private methods — mere-exposure

        /// <summary>
        /// Computes the total mere-exposure attraction bonus for a given interaction count.
        /// Logarithmic growth: fast early, levels off near <see cref="RelationshipsConfig.MereExposureSaturation"/>.
        /// </summary>
        /// <param name="count">Total positive interactions so far.</param>
        /// <param name="config">Engine configuration.</param>
        /// <returns>Total bonus in [0, <see cref="RelationshipsConfig.MereExposureMaxBoost"/>].</returns>
        private static double MereExposureBoost(int count, RelationshipsConfig config)
        {
            if (count <= 0)
            {
                return 0.0;
            }

            var saturation = Math.Log(1.0 + count)
                           / Math.Log(1.0 + config.MereExposureSaturation);

            return Math.Clamp(saturation * config.MereExposureMaxBoost, 0.0, config.MereExposureMaxBoost);
        }

        /// <summary>
        /// Rebuilds the compatibility attraction score from the explicit relationship signals.
        /// </summary>
        private static double ComputeLegacyAttraction(RelationshipEdge e)
            => Clamp(
                (e.Familiarity * 0.20)
              + (e.AestheticAttraction * 0.24)
              + (e.PhysicalAttraction * 0.24)
              + (e.RomanticInterest * 0.16)
              + (e.SexualInterest * 0.18)
              + (e.Comfort * 0.12)
              + (e.Closeness * 0.10));

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

        #endregion Private methods — mere-exposure

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

        private static double TouchBoost(TouchLevel level) => level switch
        {
            TouchLevel.Light    => +1.5,
            TouchLevel.Friendly => +3.0,
            TouchLevel.Intimate => +5.0,
            _                   =>  0.0
        };

        /// <summary>
        /// Inserts or updates an edge in the relationship graph.
        /// If the edge does not exist, initialises it with neutral default values.
        /// </summary>
        private void Upsert(HumanId self, HumanId other, Func<RelationshipEdge, RelationshipEdge> mut)
        {
            // A character cannot have a relationship with themselves.
            // Guard against self-edges that would corrupt MeanCloseness and other graph metrics.
            if (self == other)
                return;

            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);

            if (!dict.TryGetValue(other, out var e))
            {
                // Neutral defaults for an unknown person
                e = new RelationshipEdge(
                    A: self, B: other,
                    Like: 45, Trust: 45, Attraction: 35,
                    Familiarity: 10, AestheticAttraction: 35, PhysicalAttraction: 35, RomanticInterest: 35, SexualInterest: 30,
                    Closeness: 10, Respect: 55, Comfort: 40,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 0);

                using (_log.BeginScope(new CharacterLogScope(self.Value, nameof(DefaultRelationshipsEngine))))
                {
                    _log.RelEdgeCreated(self.Value.ToString(), self.Value.ToString(), other.Value.ToString());
                }
            }

            var updated = RecomputeLegacy(mut(e));

            using (_log.BeginScope(new CharacterLogScope(self.Value, nameof(DefaultRelationshipsEngine))))
            {
                _log.RelEdgeUpdated(
                    self.Value.ToString(), self.Value.ToString(), other.Value.ToString(),
                    updated.Like, updated.Trust, updated.Closeness,
                    updated.Attraction, updated.Comfort, updated.Respect);
            }

            dict[other] = updated;
            State = new RelationshipState(dict);
        }

        /// <summary>Recomputes the legacy attraction aggregate from explicit relationship signals.</summary>
        private static RelationshipEdge RecomputeLegacy(RelationshipEdge edge)
            => edge with { Attraction = ComputeLegacyAttraction(edge) };

        /// <summary>Bumps a primary relationship dimension by <paramref name="by"/> and clamps to [0, 100].</summary>
        private static double Bump(double v, double by)
            => Math.Max(0, Math.Min(100, v + by));

        /// <summary>Bumps a domain breakdown value by <paramref name="by"/> and clamps to [0, 100].</summary>
        private static double BumpD(double v, double by)
            => Math.Max(0, Math.Min(100, v + by));

        /// <summary>Linear interpolation — used during first impression for smooth blending.</summary>
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

#endif
    }
}
