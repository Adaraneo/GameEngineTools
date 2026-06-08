// DefaultInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using System;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default implementation of <see cref="IInteractionEngine"/>.
    /// </summary>
    /// <remarks>
    /// The engine reacts to two categories of events:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="ContextChanged"/> — updates the interaction environment
    ///     (location, privacy, noise, crowd).
    ///   </item>
    ///   <item>
    ///     <see cref="InteractionProposed"/> — evaluates the acceptance probability
    ///     based on relationships, psychology and the environment. The result is an <see cref="InteractionOutcome"/>.
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Why does <c>Tick()</c> do nothing?</b><br/>
    /// Interactions are reactive — they are always triggered by an event (a player or NPC initiating contact),
    /// not by the passage of time. The engine has nothing to "tick" on its own.
    /// </para>
    /// </remarks>
    internal sealed class DefaultInteractionEngine : IInteractionEngine
    {
        #region Stav a konfigurace

        /// <inheritdoc/>
        public InteractionSurface State { get; private set; }

        /// <inheritdoc/>
        public InteractionConfig Config { get; }

        #endregion Stav a konfigurace

        #region Privátní pole

        private readonly ILogger _log;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Creates a <see cref="DefaultInteractionEngine"/> instance.
        /// The initial environment state is neutral — unknown location, medium noise and crowd.
        /// </summary>
        public DefaultInteractionEngine(IOptions<InteractionConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultInteractionEngine>();

            // Safe default state — until the game sets the real context via ContextChanged
            State = new InteractionSurface(Location: "Unknown", HasPrivacy: false, Noise: 0.5, Crowding: 0.5, Kind: SurfaceKind.Unknown);
        }

        #endregion Konstruktor

        #region Tick

        /// <inheritdoc/>
        /// <remarks>Interactions are purely reactive — without an active environment nothing happens.</remarks>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Without an active environment we do nothing periodic.
        }

        #endregion Tick

        #region Handle — zpracování doménových událostí

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ContextChanged cc:
                    HandleContextChanged(cc, ctx);
                    break;

                case InteractionProposed p when p.To == ctx.Id:
                    HandleInteractionProposed(p, ctx, outbox);
                    break;

                case TouchAttempted t when t.To == ctx.Id:
                    HandleTouchAttempted(t, ctx, outbox);
                    break;

                case SexualEncounterProposed se when se.To == ctx.Id:
                    HandleSexualEncounterProposed(se, ctx, outbox);
                    break;
            }
        }

        private void HandleTouchAttempted(TouchAttempted attempted, IHumanContext ctx, IEventCollector outbox)
        {
            var rels = ctx.Snapshot.Relationships.Edges;
            rels.TryGetValue(attempted.From, out var edge);

            var closeness = edge?.Closeness ?? 0;
            var intimacyInterest = edge is null
                ? 0
                : (edge.SexualInterest * 0.65) + (edge.IntimateAffinity * 0.35);
            var comfort = edge?.Comfort ?? 0;
            var psych = ctx.Snapshot.Psychology;

            // The acceptance probability depends strongly on the touch level
            var baseP = attempted.Level switch
            {
                TouchLevel.Light => 0.5 + closeness * 0.005,
                TouchLevel.Friendly => 0.3 + closeness * 0.007 + comfort * 0.003,
                TouchLevel.Intimate => 0.05 + closeness * 0.006 + intimacyInterest * 0.007,
                _ => 0
            };

            if (attempted.Level == TouchLevel.Intimate)
            {
                baseP += SociosexualityBehaviorMath.IntimateTouchAcceptanceBias(ctx.Personality.Sociosexuality, edge, State.HasPrivacy);
            }

            // Privacy and psychology modulate it
            baseP += State.HasPrivacy ? 0.05 : -0.05;
            baseP -= psych.Stress * 0.002;
            baseP += Math.Max(0, psych.Valence) * 0.05;

            var pAcc = Math.Clamp(baseP, 0, 0.95);
            var accepted = ctx.Random.Chance(pAcc);

            // Intimate without sufficient Closeness/Attraction is always declined
            if (attempted.Level == TouchLevel.Intimate
                && SociosexualityBehaviorMath.BlocksIntimateTouch(ctx.Personality.Sociosexuality, edge))
            {
                accepted = false;
            }

            using (_log.BeginCharacterScope(
                ctx.Id.Value,
                nameof(DefaultInteractionEngine),
                relatedPersonId: attempted.From.Value,
                locationId: State.Location,
                tickKey: attempted.OccurredAt.WorldTicks.ToString()))
            {
                _log.TouchOutcomeDecided(ctx.Id.Value.ToString(), attempted.From.Value.ToString(), attempted.To.Value.ToString(), attempted.Level.ToString(), pAcc, accepted ? "PŘIJATO" : "ODMÍTNUTO");
            }

            outbox.Add(new TouchOutcome(attempted.OccurredAt, attempted.From, attempted.To, attempted.Level, accepted, accepted ? "accepted" : "declined"));
        }

        /// <summary>
        /// Updates the character's environment based on a new location / context.
        /// The Noise and Crowding values are clamped to [0, 1].
        /// </summary>
        private void HandleContextChanged(ContextChanged cc, IHumanContext ctx)
        {
            using (_log.BeginCharacterScope(
                ctx.Id.Value,
                nameof(DefaultInteractionEngine),
                locationId: cc.Location,
                tickKey: cc.OccurredAt.WorldTicks.ToString()))
            {
                _log.InteractionContextChanged(ctx.Id.Value.ToString(), cc.Location, cc.Noise, cc.Crowding);
            }

            State = new InteractionSurface(
                Location: cc.Location,
                HasPrivacy: cc.HasPrivacy,
                Noise: Math.Clamp(cc.Noise, 0, 1),
                Crowding: Math.Clamp(cc.Crowding, 0, 1),
                Kind: cc.Kind,
                Observers: cc.Observers,
                NormContext: cc.NormContext);
        }

        /// <summary>
        /// Evaluates the proposed interaction and emits an <see cref="InteractionOutcome"/>.
        /// </summary>
        /// <remarks>
        /// The acceptance probability depends on:
        /// <list type="bullet">
        ///   <item>Relationships: Closeness, Comfort, Trust (the closer, the more willing)</item>
        ///   <item>Psychology: Valence (a good mood opens up), Stress (stress closes off)</item>
        ///   <item>Environment: privacy helps, a crowd and noise hinder</item>
        ///   <item>Misattribution: stress causes misreading of intents</item>
        /// </list>
        /// <para>
        /// <b>Important:</b> the <see cref="SpeechAct"/> is carried unchanged into the <see cref="InteractionOutcome"/>,
        /// so the <c>RelationshipsEngine</c> knows which domain to update.
        /// </para>
        /// </remarks>
        private void HandleInteractionProposed(InteractionProposed p, IHumanContext ctx, IEventCollector outbox)
        {
            var rels = ctx.Snapshot.Relationships.Edges;
            rels.TryGetValue(p.From, out var edge);

            // Relationship inputs — an unknown person gets neutral default values
            var closeness = edge?.Closeness ?? 30;
            var comfort = edge?.Comfort ?? 30;
            var trust = edge?.Trust ?? 30;

            var psych = ctx.Snapshot.Psychology;
            var expectedAcceptance = (ctx.Snapshot.SemanticMemory ?? SemanticMemoryState.Empty)
                .ExpectedAcceptance(p.From, p.Act, edge, ctx.PsychologyProfile, ctx.Snapshot.Memory.Episodes);

            // Base acceptance probability — a linear combination of relationships and psychology
            // B4: Agreeableness modulates willingness to accept — high-A characters are more receptive
            // (+0.10 at full Agreeableness, −0.10 at zero); the ±0.10 range is conservative.
            var agreeablenessBias = (ctx.Personality.BigFive.Agreeableness - 0.5) * 0.20;
            var baseP = 0.30
                        + 0.0025 * closeness
                        + 0.0020 * comfort
                        + 0.0020 * trust
                        + 0.10 * Math.Max(0, psych.Valence)
                        + (State.HasPrivacy ? 0.05 : 0)
                        - 0.05 * State.Crowding
                        - 0.0015 * psych.Stress
                        + (expectedAcceptance - 0.5) * 0.25
                        + agreeablenessBias;

            if (p.Act == SpeechAct.Invite)
            {
                baseP += SociosexualityBehaviorMath.InviteAcceptanceBias(
                    ctx.Personality.Sociosexuality,
                    edge,
                    expectedAcceptance,
                    State.HasPrivacy);
                baseP += SexualOrientationBehaviorMath.InviteAcceptanceBias(ctx.AttractionProfile, p.FromBiology);

                // S2 — Basson responsive desire: long-term partners respond to advances
                // even when they wouldn't spontaneously initiate (Basson 2001).
                // ResponsiveDesireLevel [0–80] → up to +0.16 acceptance bias at max.
                if (edge is not null)
                    baseP += edge.ResponsiveDesireLevel / 500.0;  // max +0.16 at ResponsiveDesireLevel=80
            }

            // Misattribution: higher stress + noise → greater chance of misreading the intent
            // E2: noise degrades social signal quality (harder to read intent correctly).
            var noise = double.IsNaN(State.Noise) ? 0.0 : State.Noise;
            var misattrib = ComputeMisattributionPenalty(p.Act, psych.Stress, trust, comfort, State.HasPrivacy, noise);
            baseP -= misattrib;

            // ── Norm violation appraisal (Sznycer 2016 / FAtiMA double-appraisal pattern) ─────────────
            // Anticipatory shame: if the surface has an active norm context, compute the violation score
            // and penalise the acceptance probability BEFORE the random draw. This models the deterrent
            // function of shame — the character "feels the weight" of inappropriateness pre-commit.
            var normViolationScore = 0.0;
            var normContext = State.NormContext;
            if (normContext is not null)
            {
                var observerCount = State.Observers?.Count ?? 0;
                normViolationScore = NormViolationMath.ComputeViolationScore(normContext, State.HasPrivacy, observerCount);

                // Reduce recipient acceptance probability.
                // baseP penalty: p *= (1 - AcceptancePenalty), clamped so p stays above 0.05.
                var normPenalty = NormViolationMath.AcceptancePenalty(normViolationScore);
                baseP = Math.Max(0.05, baseP * (1.0 - normPenalty));

                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultInteractionEngine)))
                {
                    _log.NormViolationDetected(
                        ctx.Id.Value.ToString(),
                        normContext.Kind.ToString(),
                        normViolationScore,
                        normPenalty);
                }
            }

            var pAcc = Math.Clamp(baseP, 0.05, 0.95);
            var accepted = ctx.Random.Chance(pAcc);

            if (misattrib > 0.05)
            {
                using (_log.BeginCharacterScope(
                    ctx.Id.Value,
                    nameof(DefaultInteractionEngine),
                    relatedPersonId: p.From.Value,
                    locationId: State.Location,
                    tickKey: p.OccurredAt.WorldTicks.ToString()))
                {
                    _log.MisattributionPenaltyApplied(
                        ctx.Id.Value.ToString(),
                        p.From.Value.ToString(),
                        p.To.Value.ToString(),
                        psych.Stress, noise, misattrib);
                }
            }

            using (_log.BeginCharacterScope(
                ctx.Id.Value,
                nameof(DefaultInteractionEngine),
                relatedPersonId: p.From.Value,
                locationId: State.Location,
                tickKey: p.OccurredAt.WorldTicks.ToString()))
            {
                _log.InteractionOutcomeDecided(
                    ctx.Id.Value.ToString(),
                    p.From.Value.ToString(),
                    p.To.Value.ToString(),
                    pAcc,
                    accepted ? "PŘIJATO" : "ODMÍTNUTO");
            }

            // We carry p.Act — the RelationshipsEngine needs it for DomainBreakdown
            var (peakVal, endVal) = ComputePeakEndValence(p.Act, accepted, misattrib);
            var outcome = new InteractionOutcome(
                OccurredAt: p.OccurredAt,
                From: p.From,
                To: p.To,
                Accepted: accepted,
                Reason: accepted ? "accepted" : "declined",
                Act: p.Act,
                FromBiology: p.FromBiology,
                ToBiology: ctx.Biology,
                PeakValence: peakVal,
                EndValence: endVal);

            outbox.Add(outcome);

            // ── Norm violation post-commit: emit shame and observer cascade ───────────────────────────
            // Only fires when a norm context is active AND the violation score is meaningful.
            // Threshold 0.25 avoids spamming shame events for trivial norm infractions.
            const double NormViolationThreshold = 0.25;
            if (normContext is not null && normViolationScore >= NormViolationThreshold)
            {
                var hasAudience = (State.Observers?.Count ?? 0) > 0;

                // Shame event for the actor (consumed by DefaultPsychologyEngine).
                outbox.Add(new NormViolationOccurred(
                    p.OccurredAt,
                    Actor: p.From,
                    NormKind: normContext.Kind,
                    ViolationScore: normViolationScore,
                    HasAudience: hasAudience));

                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultInteractionEngine)))
                {
                    _log.NormViolationShameSpiked(
                        ctx.Id.Value.ToString(),
                        normContext.Kind.ToString(),
                        normViolationScore,
                        hasAudience);
                }

                // Observer cascade: one ObserverNormReaction per witness.
                // Identity sharing is unknown at this layer → default to MoralOutrage for third parties.
                // When RelationshipsEngine gains group-membership data, route VicariousShame there.
                if (State.Observers is { Count: > 0 } observers)
                {
                    foreach (var observer in observers)
                    {
                        var reactionKind = NormViolationMath.RouteObserverReaction(
                            observer,
                            actor: p.From,
                            victim: p.To,
                            sharesIdentityWithActor: false); // TODO: wire group membership when available

                        outbox.Add(new ObserverNormReaction(
                            p.OccurredAt,
                            Observer: observer,
                            Actor: p.From,
                            Victim: p.To,
                            NormKind: normContext.Kind,
                            ReactionKind: reactionKind,
                            ViolationScore: normViolationScore));

                        using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultInteractionEngine)))
                        {
                            _log.ObserverNormReactionRouted(
                                ctx.Id.Value.ToString(),
                                observer.Value.ToString(),
                                reactionKind.ToString(),
                                normViolationScore);
                        }
                    }
                }
            }

            if (accepted && p.Act == SpeechAct.Invite && HasSexualEncounterReadiness(p.OccurredAt, ctx, edge, expectedAcceptance))
            {
                var proposed = new SexualEncounterProposed(
                    p.OccurredAt,
                    p.From,
                    p.To,
                    ReproductiveIntent.Indifferent,
                    ContraceptionLevel.Unspecified,
                    HasReproductivePotential(p.FromBiology, ctx.Biology),
                    FromBiology: p.FromBiology,
                    ToBiology: ctx.Biology);

                outbox.Add(proposed);
            }
        }

        #endregion Handle — zpracování doménových událostí

        private void HandleSexualEncounterProposed(SexualEncounterProposed proposed, IHumanContext ctx, IEventCollector outbox)
        {
            ctx.Snapshot.Relationships.Edges.TryGetValue(proposed.From, out var edge);
            var expectedAcceptance = (ctx.Snapshot.SemanticMemory ?? SemanticMemoryState.Empty)
                .ExpectedAcceptance(proposed.From, SpeechAct.Invite, edge, ctx.PsychologyProfile, ctx.Snapshot.Memory.Episodes);
            var accepted = ShouldResolveSexualEncounter(proposed.OccurredAt, ctx, edge, expectedAcceptance, proposed.FromBiology);

            outbox.Add(new SexualEncounterOutcome(
                proposed.OccurredAt,
                proposed.From,
                proposed.To,
                accepted,
                accepted ? "accepted" : "declined",
                proposed.Intent,
                proposed.Contraception,
                proposed.ReproductivePotential,
                proposed.FromBiology,
                proposed.ToBiology ?? ctx.Biology));

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultInteractionEngine)))
            {
                _log.SexualEncounterProposed(ctx.Id.Value.ToString(), proposed.From.Value.ToString(), proposed.To.Value.ToString(), proposed.Intent.ToString(), proposed.ReproductivePotential.ToString(), proposed.Contraception.ToString());
            }
        }

        private bool ShouldResolveSexualEncounter(
            WDateTime occurredAt,
            IHumanContext ctx,
            RelationshipEdge? edge,
            double expectedAcceptance,
            SexBiology? targetBiology)
        {
            if (!CanConsiderSexualEncounter(occurredAt, ctx, edge))
            {
                return false;
            }

            var bias = SociosexualityBehaviorMath.InviteAcceptanceBias(ctx.Personality.Sociosexuality, edge, expectedAcceptance, State.HasPrivacy);
            bias += SexualOrientationBehaviorMath.SexualEncounterAcceptanceBias(ctx.AttractionProfile, targetBiology);
            var p = Math.Clamp(0.18 + expectedAcceptance * 0.28 + bias - ctx.Snapshot.Psychology.Stress * 0.001, 0.02, 0.82);
            return ctx.Random.Chance(p);
        }

        private bool CanConsiderSexualEncounter(WDateTime occurredAt, IHumanContext ctx, RelationshipEdge? edge)
        {
            if (!State.HasPrivacy || ctx.Snapshot.Physiology.Pain > 55 || ctx.Snapshot.Physiology.Energy < 25)
            {
                return false;
            }

            if (ctx.Snapshot.Psychology.Stress > 70 || !IsAdult(ctx, occurredAt))
            {
                return false;
            }

            return !SociosexualityBehaviorMath.BlocksIntimateTouch(ctx.Personality.Sociosexuality, edge);
        }

        private bool HasSexualEncounterReadiness(
            WDateTime occurredAt,
            IHumanContext ctx,
            RelationshipEdge? edge,
            double expectedAcceptance)
        {
            if (!CanConsiderSexualEncounter(occurredAt, ctx, edge) || edge is null)
            {
                return false;
            }

            if (ctx.Snapshot.Psychology.Stress > 35 || expectedAcceptance < 0.74)
            {
                return false;
            }

            var relationalReadiness =
                edge.Trust * 0.24
                + edge.Comfort * 0.28
                + edge.Closeness * 0.20
                + edge.IntimateAffinity * 0.14
                + edge.SexualInterest * 0.14;

            return edge.Trust >= 72
                && edge.Comfort >= 72
                && edge.Closeness >= 70
                && edge.SexualInterest >= 68
                && relationalReadiness >= 72.0;
        }

        private double ComputeMisattributionPenalty(
            SpeechAct act, double stress, double trust, double comfort, bool hasPrivacy,
            double noise = 0.0)
        {
            var stressFactor = Math.Clamp(stress / 100.0, 0.0, 1.0);
            var safety = Math.Clamp(((trust + comfort) * 0.5) / 100.0, 0.0, 1.0);
            var unsafeMultiplier = 0.45 + (1.0 - safety) * 1.10;
            var actMultiplier = act switch
            {
                SpeechAct.Invite => 1.35,
                SpeechAct.SelfDisclosure => 1.20,
                SpeechAct.Meta => 1.10,
                SpeechAct.SmallTalk or SpeechAct.Question => 0.70,
                _ => 1.0
            };
            var privacyRelief = hasPrivacy && safety >= 0.65 ? 0.78 : 1.0;

            // E2 — noise degrades social signal quality.
            // At noise=0.0: amplifier=1.0 (no change); at noise=1.0: amplifier=1+NoiseAttributionAmplifier.
            var noiseAmplifier = 1.0 + Math.Clamp(noise, 0.0, 1.0) * Config.NoiseAttributionAmplifier;

            return Config.MisattributionRateBase * stressFactor * unsafeMultiplier * actMultiplier * privacyRelief * noiseAmplifier;
        }

        private static (double peak, double end) ComputePeakEndValence(
            SpeechAct act, bool accepted, double misattribPenalty)
        {
            if (accepted)
            {
                var end = act switch
                {
                    SpeechAct.SelfDisclosure or SpeechAct.Validation or SpeechAct.Invite or SpeechAct.Meta => 0.85,
                    SpeechAct.SmallTalk or SpeechAct.Humor or SpeechAct.Question => 0.50,
                    _ => 0.40
                };
                return (1.0, end);
            }
            else
            {
                var end = misattribPenalty > 0.12 ? -0.80 : -0.45;
                end = Math.Clamp(end - misattribPenalty * 0.15, -1.0, 0.0);
                return (-1.0, end);
            }
        }

        private static bool HasReproductivePotential(SexBiology? initiatorBiology, SexBiology recipientBiology)
            => (initiatorBiology == SexBiology.Male && recipientBiology == SexBiology.Female)
                || (initiatorBiology == SexBiology.Female && recipientBiology == SexBiology.Male);

        private static bool IsAdult(IHumanContext ctx, WDateTime now)
            => ctx.Identity is not null
                && StadiumResolver.Resolve(AgeYears(ctx.Identity.BirthDate, now.Date)) is StadiumType.Adult or StadiumType.MidAged or StadiumType.Old;

        private static int AgeYears(WDateOnly birth, WDateOnly today)
        {
            var age = today.Year - birth.Year;
            if (today.Month < birth.Month ||
                (today.Month == birth.Month && today.Day < birth.Day))
            {
                age--;
            }

            return Math.Max(0, age);
        }

        #region RestoreState

        /// <inheritdoc/>
        public void RestoreState(InteractionSurface state) => State = state;

        #endregion RestoreState
    }
}
