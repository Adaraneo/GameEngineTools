// DefaultMemoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Engines.ToM;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static ActionNames;

    /// <summary>
    /// Default implementation of the memory engine.
    ///
    /// Implements three cognitively realistic principles:
    /// <list type="bullet">
    ///   <item><b>The Ebbinghaus forgetting curve</b> — an exponential decline in memory strength,
    ///         not linear. Fresh memories decay faster than well-anchored ones.</item>
    ///   <item><b>Sleep-linked consolidation</b> — reinforcement of the most salient episodes is
    ///         triggered by the <see cref="GameEngineTools.Characters.Engines.Sleep.SleepEnded"/> event, not by 24 hours elapsing.</item>
    ///   <item><b>Reinforcement (spacing effect)</b> — a repeated experience of the same kind does not create
    ///         duplicate records but reinforces the existing episode and updates its timestamp.</item>
    /// </list>
    /// </summary>
    internal sealed class DefaultMemoryEngine : IMemoryEngine
    {
        #region Stav a konfigurace

        /// <summary>Current memory state — the list of episodes.</summary>
        public MemoryIndex State { get; private set; }

        /// <summary>Engine configuration (forgetting rate, boost, prune threshold).</summary>
        public MemoryConfig Config { get; }

        #endregion Stav a konfigurace

        #region Privátní pole

        private readonly ILogger _log;
        private readonly IMemoryFidelityPolicy? _memoryFidelityPolicy;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Creates a <see cref="DefaultMemoryEngine"/> instance.
        /// </summary>
        /// <param name="cfg">Configuration injected via the Options pattern.</param>
        /// <param name="loggerFactory">Logger factory — enables a per-character scope.</param>
        public DefaultMemoryEngine(
            IOptions<MemoryConfig> cfg,
            ILoggerFactory loggerFactory,
            IMemoryFidelityPolicy? memoryFidelityPolicy = null)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultMemoryEngine>();
            _memoryFidelityPolicy = memoryFidelityPolicy;

            // Initialize an empty state — no memories
            State = new MemoryIndex(
                new List<EpisodicMemory>());
        }

        #endregion Konstruktor

        #region Veřejné API

        /// <summary>
        /// Encodes a new episode into memory.
        ///
        /// If an episode with the same <c>Kind</c> key already exists and is still strong
        /// (above the prune threshold), it applies <b>reinforcement</b> — strengthening the existing record
        /// and updating its timestamp. This models the spacing effect:
        /// a repeated experience consolidates the memory instead of producing duplicates.
        /// </summary>
        /// <param name="episode">The episode to encode.</param>
        /// <param name="ctx">Kontext postavy (ID, snapshot).</param>
        /// <param name="outbox">Output queue of domain events.</param>
        public void Encode(EpisodicMemory episode, IHumanContext ctx, IEventCollector outbox)
        {
            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultMemoryEngine)))
            {
                if (episode.OtherPerson == ctx.Id)
                {
                    episode = episode with { OtherPerson = null };
                }

                var episodes = State.Episodes.ToList();

                // --- REINFORCEMENT (spacing effect) ---
                // We do not match by the raw Kind string,
                // but by the explicit reinforcement key.
                var incomingKey = MemoryReinforcementKeyBuilder.From(episode);

                var existingIndex = episodes.FindIndex(e =>
                    e.Strength >= Config.PruneThreshold
                    && MemoryReinforcementKeyBuilder.From(e) == incomingKey);

                if (existingIndex >= 0)
                {
                    // We reinforce the existing memory instead of creating a new record.
                    // Logic: a repeated experience consolidates the trace but does not clamp above 1.0.
                    var existing = episodes[existingIndex];
                    var reinforced = existing with
                    {
                        Strength = Math.Min(1.0, existing.Strength + Config.ReinforcementBoost),

                        // Update the timestamp - "it last happened now"
                        When = episode.When,

                        // Keep the latest representation of raw Kind / PerceivedWhat.
                        // Thanks to the explicit reinforcement key, Kind no longer needs to be the identity.
                        What = episode.What,
                        PerceivedWhat = episode.PerceivedWhat ?? existing.PerceivedWhat,

                        Distortion = Math.Max(existing.Distortion, episode.Distortion),
                        RecallConfidence = Math.Min(existing.RecallConfidence, episode.RecallConfidence)
                    };

                    episodes[existingIndex] = reinforced;
                    State = new MemoryIndex(episodes) { Knowledge = State.Knowledge, KnownObjects = State.KnownObjects };

                    _log.MemoryEncoded(
                        ctx.Id.Value.ToString(),
                        episode.What,
                        reinforced.Strength,
                        reinforced.Emotion.ToString(),
                        reinforced.PerceivedWhat ?? episode.What,
                        reinforced.Distortion);

                    // Raise the event for reinforcement too — Strength has been updated
                    outbox.Add(new MemoryEncoded(episode.When, ctx.Id, existing.Id, reinforced.Strength, episode.What, reinforced.PerceivedWhat, reinforced.OtherPerson, reinforced.BeliefEvidence));
                    return;
                }

                // --- NEW EPISODE ---
                var encoded = episode with
                {
                    PerceivedWhat = episode.PerceivedWhat ?? BuildPerceivedWhat(episode, ctx),
                    Distortion = Math.Max(0.0, episode.Distortion + ComputeDistortion(ctx, episode)),
                    RecallConfidence = Math.Clamp(episode.RecallConfidence - ComputeDistortion(ctx, episode) * 0.5, 0.2, 1.0)
                };

                episodes.Add(encoded);
                State = new MemoryIndex(episodes) { Knowledge = State.Knowledge, KnownObjects = State.KnownObjects };

                _log.MemoryEncoded(
                    ctx.Id.Value.ToString(),
                    encoded.What,
                    encoded.Salience,
                    encoded.Emotion.ToString(),
                    encoded.PerceivedWhat ?? encoded.What,
                    encoded.Distortion);

                outbox.Add(new MemoryEncoded(encoded.When, ctx.Id, encoded.Id, encoded.Strength, encoded.What, encoded.PerceivedWhat, encoded.OtherPerson, encoded.BeliefEvidence));
            }
        }

        /// <summary>
        /// Returns the memories satisfying the given predicate.
        /// Used by the BehaviorEngine to influence the character's decisions.
        /// </summary>
        /// <param name="predicate">The filter predicate.</param>
        /// <returns>The filtered list of episodes (a read-only snapshot).</returns>
        public IReadOnlyList<EpisodicMemory> Recall(Func<EpisodicMemory, bool> predicate)
            => State.Episodes.Where(predicate).Select(Reconstruct).ToList();

        public MemoryRecallResult Recall(MemoryRecallQuery query, WDateTime now)
            => MemoryCognition.Recall(State, query, now);

        public DecisionWorkingSet BuildWorkingSet(MemoryRecallQuery query, WDateTime now)
            => MemoryCognition.BuildWorkingSet(State, query, now);

        public DecisionWorkingSet BuildWorkingSet(MemoryRecallQuery query, WDateTime now, IHumanContext ctx)
        {
            var burden = ComputeCognitiveBurden(ctx);
            var conscientiousness = ctx.Personality.BigFive.Conscientiousness;
            var threshold = Config.CognitiveBurdenThreshold + (conscientiousness - 0.5) * 0.10;
            var enriched = query with
            {
                CognitiveBurden = burden,
                CurrentValence = ctx.Snapshot.Psychology.Valence,
                NeuroticismScore = ctx.Personality.BigFive.Neuroticism,
                DaysInNegativeMood = ComputeDaysInNegativeMood(ctx.Snapshot.Memory.Episodes, now),
                MoodCongruenceWeight = Config.MoodCongruenceWeight,
                DepressionNegativeBiasThreshold = Config.DepressionNegativeBiasThreshold
            };
            return MemoryCognition.BuildWorkingSet(State, enriched, now, threshold);
        }

        /// <inheritdoc/>
        public bool KnowsAbout(HumanId subject, string actionKind, HumanId? objectId = null)
            => ConfidenceAbout(subject, actionKind, objectId) >= Config.KnowledgePruneThreshold;

        /// <inheritdoc/>
        public double ConfidenceAbout(HumanId subject, string actionKind, HumanId? objectId = null)
        {
            var best = 0.0;
            foreach (var f in State.Knowledge)
            {
                if (f.Subject != subject) continue;
                if (f.ActionKind != actionKind) continue;
                if (objectId.HasValue && f.Object.HasValue && f.Object.Value != objectId.Value) continue;
                if (f.Confidence > best) best = f.Confidence;
            }
            return best;
        }

        #endregion Veřejné API

        #region Handle — zpracování doménových událostí

        /// <summary>
        /// Reacts to domain events from other engines and encodes them as episodic memories.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The <c>Kind</c> schema:</b> every event is translated into a deterministic semantic key
        /// via <see cref="MemoryWhatParser"/>. Format: <c>{Category}:{Type}:{Outcome}|{key}={value}</c>
        /// </para>
        /// <para>
        /// <b>Why a deterministic key?</b>
        /// <see cref="Encode"/> uses <c>Kind</c> as the reinforcement key (spacing effect) —
        /// a repeated experience of the same type reinforces the existing memory instead of creating a new one.
        /// If the key were different every time (e.g. it contained a timestamp), reinforcement would not work.
        /// </para>
        /// <para>
        /// <b>Encoded event types:</b>
        /// <list type="bullet">
        ///   <item><see cref="ActionCommitted"/> — every performed action, with salience by importance.</item>
        ///   <item><see cref="InteractionOutcome"/> — acceptance/rejection of an interaction between characters.</item>
        ///   <item><see cref="FirstImpressionFormed"/> — a first meeting with a new character.</item>
        ///   <item><see cref="MicroPositive"/> / <see cref="MicroNegative"/> — mikrointerakce.</item>
        ///   <item><see cref="RepairAttempt"/> — a repair attempt.</item>
        ///   <item><see cref="NightmareTriggered"/> — a nightmare (high salience, negative emotion).</item>
        ///   <item><see cref="GameEngineTools.Characters.Engines.Sleep.SleepEnded"/> — triggers memory consolidation.</item>
        /// </list>
        /// </para>
        /// </remarks>
        private EpisodicMemory Reconstruct(EpisodicMemory episode)
        {
            if (episode.Distortion <= 0.01)
            {
                return episode with { PerceivedWhat = episode.PerceivedWhat ?? episode.What };
            }

            return episode with
            {
                PerceivedWhat = episode.PerceivedWhat ?? episode.What,
                RecallConfidence = Math.Clamp(episode.RecallConfidence - episode.Distortion * 0.15, 0.1, 1.0)
            };
        }

        private double ComputeDistortion(IHumanContext ctx, EpisodicMemory episode)
        {
            var stress = ctx.Snapshot.Psychology.Stress / 100.0;
            var emotionalWeight = episode.Emotion switch
            {
                EmotionalTag.Negative => 1.0,
                EmotionalTag.Mixed => 0.8,
                EmotionalTag.Positive => 0.35,
                _ => 0.2
            };

            return Math.Clamp(stress * emotionalWeight * Config.StressDistortionWeight, 0.0, 0.8);
        }

        private string BuildPerceivedWhat(EpisodicMemory episode, IHumanContext ctx)
        {
            var distortion = ComputeDistortion(ctx, episode);
            if (distortion < 0.1)
            {
                return episode.What;
            }

            return episode.Emotion switch
            {
                EmotionalTag.Negative => $"PerceivedThreat:{episode.What}",
                EmotionalTag.Mixed => $"PerceivedSlight:{episode.What}",
                EmotionalTag.Positive when ctx.Snapshot.Psychology.Valence > 0.3 => $"PerceivedWarmth:{episode.What}",
                _ => episode.What
            };
        }

        private static HumanId? ResolveOtherPerson(HumanId self, HumanId a, HumanId b)
            => self == a ? b : self == b ? a : b;

        private static PersonBeliefEvidence? CreateFirstImpressionBeliefEvidence(HumanId self, FirstImpressionFormed impression)
        {
            var other = ResolveOtherPerson(self, impression.A, impression.B);
            if (other is null)
            {
                return null;
            }

            return impression.Like >= 70
                ? new PersonBeliefEvidence(other.Value, PersonBeliefKind.Warm, 0.18, "first-impression-positive")
                : impression.Like < 45
                    ? new PersonBeliefEvidence(other.Value, PersonBeliefKind.Critical, 0.12, "first-impression-negative")
                    : null;
        }

        private static PersonBeliefEvidence? CreateInteractionBeliefEvidence(HumanId self, InteractionOutcome outcome)
        {
            if (outcome.From == self)
            {
                return outcome.Accepted
                    ? new PersonBeliefEvidence(
                        outcome.To,
                        outcome.Act is SpeechAct.SelfDisclosure or SpeechAct.Validation or SpeechAct.Meta ? PersonBeliefKind.EmotionallySafe : PersonBeliefKind.Warm,
                        outcome.Act is SpeechAct.Invite ? 0.24 : 0.18,
                        $"interaction-accepted:{outcome.Act}")
                    : new PersonBeliefEvidence(
                        outcome.To,
                        outcome.Act is SpeechAct.SelfDisclosure or SpeechAct.Validation ? PersonBeliefKind.Critical : PersonBeliefKind.Rejecting,
                        outcome.Act is SpeechAct.SelfDisclosure or SpeechAct.Invite ? 0.24 : 0.18,
                        $"interaction-rejected:{outcome.Act}");
            }

            if (outcome.To == self && outcome.Accepted)
            {
                return new PersonBeliefEvidence(
                    outcome.From,
                    outcome.Act is SpeechAct.Validation or SpeechAct.SelfDisclosure ? PersonBeliefKind.EmotionallySafe : PersonBeliefKind.Warm,
                    0.16,
                    $"interaction-received:{outcome.Act}");
            }

            return null;
        }

        //private static PersonBeliefEvidence CreateMicroBeliefEvidence(HumanId self, HumanId a, HumanId b, bool positive, string source)
        //{
        //    var other = ResolveOtherPerson(self, a, b) ?? b;
        //    return positive
        //        ? new PersonBeliefEvidence(other, source.Contains("help", StringComparison.OrdinalIgnoreCase) ? PersonBeliefKind.Reliable : PersonBeliefKind.Warm, 0.14, $"micro-positive:{source}")
        //        : new PersonBeliefEvidence(other, source.Contains("ignore", StringComparison.OrdinalIgnoreCase) || source.Contains("cold", StringComparison.OrdinalIgnoreCase) ? PersonBeliefKind.Rejecting : PersonBeliefKind.Critical, 0.16, $"micro-negative:{source}");
        //}

        private static PersonBeliefEvidence CreateMicroBeliefEvidence(HumanId self, HumanId a, HumanId b, bool positive, string kind)
        {
            var other = ResolveOtherPerson(self, a, b) ?? b;
            var normalized = kind.Trim().ToLowerInvariant();

            PersonBeliefKind beliefKind;

            if (positive)
            {
                beliefKind = normalized switch
                {
                    MemoryMicroEventKinds.Help => PersonBeliefKind.Reliable,
                    MemoryMicroEventKinds.Support => PersonBeliefKind.Reliable,
                    MemoryMicroEventKinds.Repair => PersonBeliefKind.Reliable,

                    MemoryMicroEventKinds.Warmth => PersonBeliefKind.EmotionallySafe,
                    MemoryMicroEventKinds.Validation => PersonBeliefKind.EmotionallySafe,

                    _ => PersonBeliefKind.Warm
                };

                return new PersonBeliefEvidence(
                    other,
                    beliefKind,
                    0.14,
                    $"micro-positive:{normalized}");
            }

            beliefKind = normalized switch
            {
                MemoryMicroEventKinds.Ignore => PersonBeliefKind.Rejecting,
                MemoryMicroEventKinds.Cold => PersonBeliefKind.Rejecting,
                MemoryMicroEventKinds.Dismissal => PersonBeliefKind.Rejecting,

                MemoryMicroEventKinds.Criticism => PersonBeliefKind.Critical,
                MemoryMicroEventKinds.Slight => PersonBeliefKind.Critical,

                _ => PersonBeliefKind.Critical
            };

            return new PersonBeliefEvidence(
                other,
                beliefKind,
                0.16,
                $"micro-negative:{normalized}");
        }

        private static PersonBeliefEvidence CreateRepairBeliefEvidence(HumanId self, RepairAttempt attempt)
        {
            var other = ResolveOtherPerson(self, attempt.A, attempt.B) ?? attempt.B;
            return attempt.Accepted
                ? new PersonBeliefEvidence(other, PersonBeliefKind.Reliable, 0.20, "repair-accepted")
                : new PersonBeliefEvidence(other, PersonBeliefKind.Rejecting, 0.18, "repair-rejected");
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (!ShouldStoreEvent(@event, ctx))
            {
                return;
            }

            switch (@event)
            {
                // ── Akce ─────────────────────────────────────────────────────────────────
                case ActionCommitted ac:
                    {
                        // Own action — the simplest schema, no actors
                        var what = MemoryWhatParser.Action(ac.ActionName);
                        var other = ac.TargetHuman == ctx.Id ? null : ac.TargetHuman;
                        var acSalience = SalienceForAction(ac.ActionName, ctx);
                        var acEmotion = EmotionFor(ac.ActionName, ctx.Snapshot.Psychology.Valence);

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            ac.OccurredAt,
                            what,
                            acSalience,
                            acEmotion,
                            Strength: ComputeInitialStrength(acSalience, acEmotion),
                            OtherPerson: other),
                            ctx, outbox);
                        break;
                    }

                // ── Interakce ─────────────────────────────────────────────────────────────
                case InteractionOutcome io:
                    {
                        // The schema captures: act type, outcome, both actors
                        // Salience is computed with the peak-end formula (Fredrickson & Kahneman 1993)
                        var what = MemoryWhatParser.Interaction(io.Act.ToString(), io.Accepted, io.From.Value, io.To.Value);
                        var salience = ComputePeakEndSalience(io);
                        var emotion = io.Accepted ? EmotionalTag.Positive : EmotionalTag.Negative;

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            io.OccurredAt,
                            what,
                            salience,
                            emotion,
                            Strength: ComputeInitialStrength(salience, emotion),
                            OtherPerson: ResolveOtherPerson(ctx.Id, io.From, io.To),
                            BeliefEvidence: CreateInteractionBeliefEvidence(ctx.Id, io),
                            PeakEmotion: ValenceToEmotionalTag(io.PeakValence),
                            EndEmotion: ValenceToEmotionalTag(io.EndValence)),
                            ctx, outbox);

                        // ToM L1+L2: record knowledge that the other party performed a SelfDisclosure.
                        // Both parties were present, so this is mutually known (common ground) — set L2.
                        if (io.Act == SpeechAct.SelfDisclosure && io.Accepted)
                        {
                            var otherId = io.From == ctx.Id ? io.To : io.From;
                            RecordKnowledge(otherId, ctx.Id, "SelfDisclosure", FactSource.DirectWitness, io.OccurredAt,
                                sharedWith: otherId, outbox: outbox, selfId: ctx.Id);
                        }
                        break;
                    }

                // ── First impression ──────────────────────────────────────────────────────
                case FirstImpressionFormed fi:
                    {
                        // First meeting — always high salience; the emotion depends on Like
                        var what = MemoryWhatParser.FirstImpression(fi.Like, fi.B.Value);
                        var emotion = fi.Like >= 70 ? EmotionalTag.Positive
                                    : fi.Like >= 45 ? EmotionalTag.Neutral
                                    : EmotionalTag.Negative;

                        var other = ResolveOtherPerson(ctx.Id, fi.A, fi.B);

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            fi.OccurredAt,
                            what,
                            Salience: 0.85,   // první dojem je velmi salinetní — evolučně důležitý
                            emotion,
                            Strength: ComputeInitialStrength(0.85, emotion),
                            OtherPerson: other,
                            BeliefEvidence: CreateFirstImpressionBeliefEvidence(ctx.Id, fi)),
                            ctx, outbox);
                        break;
                    }

                // ── Mikrointerakce ────────────────────────────────────────────────────────
                case MicroPositive mp:
                    {
                        var fromId = ctx.Id == mp.A ? mp.B.Value : mp.A.Value;
                        var what = MemoryWhatFactory.RelationMicroPositive(mp.Kind, new HumanId(fromId), ctx.Id);

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            mp.OccurredAt,
                            what,
                            Salience: 0.6,
                            EmotionalTag.Positive,
                            Strength: ComputeInitialStrength(0.6, EmotionalTag.Positive),
                            OtherPerson: ResolveOtherPerson(ctx.Id, mp.A, mp.B),
                            BeliefEvidence: CreateMicroBeliefEvidence(ctx.Id, mp.A, mp.B, positive: true, mp.Kind)),
                            ctx, outbox);
                        break;
                    }

                case MicroNegative mn:
                    {
                        // Negative micro-interaction — slightly higher salience than a positive one
                        // (negativity bias: we remember unpleasant things better)
                        var fromId = ctx.Id == mn.A ? mn.B.Value : mn.A.Value;
                        var what = MemoryWhatFactory.RelationMicroNegative(mn.Kind, new HumanId(fromId), ctx.Id);

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            mn.OccurredAt,
                            what,
                            Salience: 0.65,
                            EmotionalTag.Negative,
                            Strength: ComputeInitialStrength(0.65, EmotionalTag.Negative),
                            OtherPerson: ResolveOtherPerson(ctx.Id, mn.A, mn.B),
                            BeliefEvidence: CreateMicroBeliefEvidence(ctx.Id, mn.A, mn.B, positive: false, mn.Kind)),
                            ctx, outbox);

                        // ToM: if I witnessed someone else act negatively toward another, record it
                        if (mn.A != ctx.Id)
                            RecordKnowledge(mn.A, mn.B, "NegativeAct", FactSource.DirectWitness, mn.OccurredAt);
                        break;
                    }

                // ── Reconciliation ────────────────────────────────────────────────────────
                case RepairAttempt ra:
                    {
                        // Reconciliation or its rejection — both are relationship turning points
                        var what = MemoryWhatParser.RepairAttempt(ra.Accepted, ra.B.Value);
                        var emotion = ra.Accepted ? EmotionalTag.Positive : EmotionalTag.Mixed;

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            ra.OccurredAt,
                            what,
                            Salience: 0.8,   // smíření/odmítnutí smíru je výrazná událost
                            emotion,
                            Strength: ComputeInitialStrength(0.8, emotion),
                            OtherPerson: ResolveOtherPerson(ctx.Id, ra.A, ra.B),
                            BeliefEvidence: CreateRepairBeliefEvidence(ctx.Id, ra)),
                            ctx, outbox);
                        break;
                    }

                // ── Sexual encounter ──────────────────────────────────────────────────────
                case SexualEncounterOutcome se:
                    {
                        var other = ResolveOtherPerson(ctx.Id, se.From, se.To);
                        var what = $"SexualEncounter:{(se.Accepted ? "Accepted" : "Declined")}|from={se.From.Value}|to={se.To.Value}";

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            se.OccurredAt,
                            what,
                            Salience: 0.95,
                            se.Accepted ? EmotionalTag.Positive : EmotionalTag.Mixed,
                            Strength: ComputeInitialStrength(0.95, se.Accepted ? EmotionalTag.Positive : EmotionalTag.Mixed),
                            OtherPerson: other,
                            BeliefEvidence: other.HasValue
                                ? se.Accepted
                                    ? new PersonBeliefEvidence(other.Value, PersonBeliefKind.EmotionallySafe, 0.18, "sexual-encounter-accepted")
                                    : new PersonBeliefEvidence(other.Value, PersonBeliefKind.Rejecting, 0.18, "sexual-encounter-declined")
                                : null),
                            ctx,
                            outbox);

                        // ToM: record mutual knowledge of sexual encounter
                        if (se.Accepted)
                            RecordKnowledge(se.From, se.To, nameof(SexualEncounterOutcome), FactSource.DirectWitness, se.OccurredAt);
                        break;
                    }

                // ── Gossip / third-party observation (Theory of Mind) ─────────────────────
                case ThirdPartyActionObserved tpa when tpa.Observer == ctx.Id:
                    {
                        var actionKind = tpa.Type switch
                        {
                            ThirdPartyObservationType.Betrayal => "Betrayal",
                            ThirdPartyObservationType.NegativeAct => "NegativeAct",
                            ThirdPartyObservationType.PositiveAct => "PositiveAct",
                            _ => "Unknown"
                        };
                        RecordKnowledge(tpa.Actor, tpa.Target, actionKind, FactSource.Gossip, tpa.OccurredAt);
                        break;
                    }

                case NightmareTriggered nt:
                    {
                        // Nightmare — the highest salience of the sleep events
                        // The character remembers it clearly; it raises stress the next day too
                        var what = MemoryWhatParser.Nightmare(nt.StressAtSleepStart);

                        Encode(new EpisodicMemory(
                            Guid.NewGuid(),
                            nt.OccurredAt,
                            what,
                            Salience: 0.9,
                            EmotionalTag.Negative,
                            Strength: ComputeInitialStrength(0.9, EmotionalTag.Negative)),
                            ctx, outbox);
                        break;
                    }

                case MemoryRecalled mr:
                    {
                        var episodes = State.Episodes.ToList();
                        var idx = episodes.FindIndex(e => e.Id == mr.EpisodeId);
                        if (idx >= 0)
                        {
                            var ep = episodes[idx];

                            // Memory reconsolidation (Nader et al. 2000): each recall
                            // drifts the memory's emotion toward the current mood.
                            // Negative memories drift 1.3× faster.
                            var currentValence = ctx.Snapshot.Psychology.Valence;
                            var driftRate = Config.ReconsolidationDriftRate
                                            * (ep.Emotion == EmotionalTag.Negative ? 1.3 : 1.0);
                            var numericEmotion = ep.Emotion switch
                            {
                                EmotionalTag.Positive => 1.0,
                                EmotionalTag.Negative => -1.0,
                                EmotionalTag.Mixed => -0.3,
                                _ => 0.0
                            };
                            var drifted = numericEmotion + (currentValence - numericEmotion) * driftRate;
                            var newEmotion = drifted switch
                            {
                                > 0.35 => EmotionalTag.Positive,
                                < -0.35 => EmotionalTag.Negative,
                                < 0.0 => EmotionalTag.Mixed,
                                _ => EmotionalTag.Neutral
                            };

                            episodes[idx] = ep with
                            {
                                RecallConfidence = Math.Clamp(ep.RecallConfidence + 0.03, 0.0, 1.0),
                                Emotion = newEmotion
                            };
                            State = new MemoryIndex(episodes) { Knowledge = State.Knowledge, KnownObjects = State.KnownObjects };

                            if (newEmotion != ep.Emotion)
                            {
                                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultMemoryEngine)))
                                {
                                    _log.MemoryReconsolidated(
                                        ctx.Id.Value.ToString(),
                                        ep.What,
                                        ep.Emotion.ToString(),
                                        newEmotion.ToString(),
                                        driftRate);
                                }
                            }
                        }
                        break;
                    }

                // ── Consolidation after sleep ─────────────────────────────────────────────
                // SleepEnded does not trigger encoding of a new memory — it consolidates existing ones.
                // See ConsolidateMemories() — it reinforces the top-N episodes by salience.
                case SleepEnded se:
                    ConsolidateMemories(se.OccurredAt, ctx, outbox);
                    break;

                // ── Object interactions — spatial memory ──────────────────────────────────
                case ObjectTaken taken when taken.Actor == ctx.Id:
                    UpdateObjectLocationFact(taken.ObjectId, locationId: null, taken.OccurredAt, confidence: 1.0, itemKind: PickupItemKind.None);
                    break;

                case ObjectUsed used when used.Actor == ctx.Id && used.WasConsumed:
                    RemoveObjectLocationFact(used.ObjectId);
                    break;

                case ObjectDropped dropped when dropped.Actor == ctx.Id:
                    UpdateObjectLocationFact(dropped.ObjectId, dropped.AtLocationId, dropped.OccurredAt, confidence: 1.0, itemKind: PickupItemKind.None);
                    break;

                case Interactions.NormViolationOccurred nv:
                    HandleNormViolation(nv, ctx, outbox);
                    break;
            }
        }

        #endregion Handle — zpracování doménových událostí

        #region Tick — zapomínání (Ebbinghausova křivka)

        /// <summary>
        /// Called every game tick. Applies an exponential decline in memory strength
        /// and prunes memories below the threshold.
        ///
        /// <b>Why exponential, not linear?</b>
        /// The Ebbinghaus forgetting curve shows that memories decay
        /// fastest shortly after encoding and then ever more slowly. The exponential
        /// function <c>e^(-k*t)</c> models this behaviour exactly:
        /// a strong memory (Strength=1.0) decays slowly,
        /// a weak one (Strength=0.1) disappears quickly.
        /// </summary>
        /// <param name="now">Current game time.</param>
        /// <param name="dt">Tick duration.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Output queue of events.</param>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var hours = Math.Max(0, dt.TotalHours);

            var episodes = State.Episodes.ToList();

            for (int i = 0; i < episodes.Count; i++)
            {
                var e = episodes[i];

                var emotionMod = e.Emotion switch
                {
                    EmotionalTag.Negative => Config.EmotionDecayMod,
                    EmotionalTag.Mixed => Config.EmotionDecayMod + 0.2,
                    EmotionalTag.Positive => Config.EmotionDecayMod + 0.3,
                    _ => 1.0
                };

                var decayFactor = Math.Exp(-Config.ForgettingRate * emotionMod * (hours / 24.0));
                var newStrength = Math.Max(0.0, e.Strength * decayFactor);

                episodes[i] = e with { Strength = newStrength };
            }

            // Pruning — removes episodes below the threshold so memory does not grow indefinitely
            episodes = episodes
                .Where(e => e.Strength >= Config.PruneThreshold)
                .ToList();

            // Preserve existing Knowledge facts when rebuilding episodic state
            State = new MemoryIndex(episodes) { Knowledge = State.Knowledge, KnownObjects = State.KnownObjects };

            // Knowledge fact confidence decay (Theory of Mind: facts fade slowly over time)
            if (State.Knowledge.Count > 0)
            {
                var decayedKnowledge = State.Knowledge
                    .Select(f =>
                    {
                        var newConf = f.Confidence - Config.KnowledgeConfidenceDecayPerDay * (hours / 24.0);
                        return f with { Confidence = newConf };
                    })
                    .Where(f => f.Confidence >= Config.KnowledgePruneThreshold)
                    .ToList();

                State = State with { Knowledge = decayedKnowledge };
            }

            // Object location fact confidence decay (spatial memory degrades over time)
            if (State.KnownObjects.Count > 0)
            {
                const double objectPruneThreshold = 0.01;
                var decayedObjects = State.KnownObjects
                    .Select(f =>
                    {
                        var newConf = f.Confidence - Config.ObjectLocationDecayPerDay * (hours / 24.0);
                        return f with { Confidence = newConf };
                    })
                    .Where(f => f.Confidence >= objectPruneThreshold)
                    .ToList();

                State = State with { KnownObjects = decayedObjects };
            }
        }

        #endregion Tick — zapomínání (Ebbinghausova křivka)

        #region Obnovení stavu

        /// <summary>
        /// Restores the engine state from a snapshot (e.g. when loading a saved game).
        /// </summary>
        /// <param name="state">The previous memory state.</param>
        public void RestoreState(MemoryIndex state) => State = state;

        #endregion Obnovení stavu

        #region Prostorová paměť objektů

        /// <summary>
        /// Records or updates the spatial memory of where an object was last seen.
        /// Pass <c>null</c> for <paramref name="locationId"/> when the object is now held by this character.
        /// </summary>
        private void UpdateObjectLocationFact(
            string objectId,
            string? locationId,
            WDateTime seenAt,
            double confidence,
            PickupItemKind itemKind)
        {
            var facts = State.KnownObjects.ToList();
            var idx = facts.FindIndex(f => f.ObjectId == objectId);
            var updated = new ObjectLocationFact(
                objectId,
                locationId ?? string.Empty,
                seenAt,
                Math.Clamp(confidence, 0.0, 1.0),
                itemKind);

            if (idx >= 0)
                facts[idx] = updated;
            else
                facts.Add(updated);

            State = State with { KnownObjects = facts };
        }

        /// <summary>
        /// Removes an object from spatial memory (e.g. after it was consumed).
        /// </summary>
        private void RemoveObjectLocationFact(string objectId)
        {
            var facts = State.KnownObjects.Where(f => f.ObjectId != objectId).ToList();
            State = State with { KnownObjects = facts };
        }

        #endregion Prostorová paměť objektů

        #region Privátní metody

        private bool ShouldStoreEvent(IDomainEvent @event, IHumanContext ctx)
            => _memoryFidelityPolicy?.ShouldStoreEvent(ctx, @event) ?? true;

        /// <summary>
        /// Records or reinforces a knowledge fact (Theory of Mind).
        /// If a fact with the same (subject, object, actionKind) already exists, confidence is boosted
        /// and the timestamp refreshed. Otherwise a new fact is created.
        /// </summary>
        private void RecordKnowledge(
            HumanId subject, HumanId? objectId, string actionKind,
            FactSource source, WDateTime now,
            HumanId? sharedWith = null, IEventCollector? outbox = null, HumanId? selfId = null)
        {
            var confidence = source == FactSource.DirectWitness
                ? Config.DirectWitnessConfidence
                : Config.GossipConfidence;

            // Level-2 ToM: a co-witness means this fact is common ground (mutually known).
            var mutual = sharedWith is not null;

            // Merge with existing fact if same (subject, object, actionKind) — boost confidence
            var existing = State.Knowledge
                .FirstOrDefault(f => f.Subject == subject
                                  && f.ActionKind == actionKind
                                  && f.Object == objectId);

            List<KnowledgeFact> updated;
            if (existing != null)
            {
                updated = State.Knowledge.ToList();
                var idx = updated.IndexOf(existing);
                updated[idx] = existing with
                {
                    Confidence = Math.Min(1.0, Math.Max(existing.Confidence, confidence)),
                    LearnedAt = now,  // refresh timestamp
                    IsMutuallyKnown = existing.IsMutuallyKnown || mutual,
                    KnownSharedWith = mutual ? sharedWith : existing.KnownSharedWith
                };
            }
            else
            {
                updated = State.Knowledge.ToList();
                updated.Add(new KnowledgeFact(
                    Id: Guid.NewGuid(),
                    LearnedAt: now,
                    Subject: subject,
                    Object: objectId,
                    ActionKind: actionKind,
                    Source: source,
                    Confidence: confidence,
                    IsMutuallyKnown: mutual,
                    KnownSharedWith: mutual ? sharedWith : null));
            }
            State = State with { Knowledge = updated };

            if (mutual && outbox is not null && selfId is not null)
            {
                outbox.Add(new MutualKnowledgeFormed(
                    now, selfId.Value, subject, sharedWith!.Value, actionKind));
            }
        }

        /// <summary>Encodes a norm-violation episode, scaling salience by the violation score.</summary>
        private void HandleNormViolation(
            Interactions.NormViolationOccurred nv,
            IHumanContext ctx,
            IEventCollector outbox)
        {
            // Compute salience based on norm kind and violation score
            var baseSalience = nv.ViolationScore * 0.8;

            // Severity modulation by norm kind
            var kindMult = nv.NormKind switch
            {
                Interactions.SocialNormKind.RitualContext => 1.3,
                Interactions.SocialNormKind.Intimacy => 1.3,
                Interactions.SocialNormKind.HarmCare => 1.3,
                Interactions.SocialNormKind.Honesty => 1.3,
                Interactions.SocialNormKind.Authority => 1.0,
                Interactions.SocialNormKind.Reciprocity => 1.0,
                Interactions.SocialNormKind.FamilyRole => 1.0,
                _ => 0.7  // Greeting, PublicConduct
            };

            var salience = baseSalience * kindMult;

            // Audience modulation
            if (nv.HasAudience)
                salience *= 1.15;

            // Clamp to [0.6, 1.0] — norm violations are always significant
            salience = Math.Clamp(salience, 0.6, 1.0);

            // Emotional intensity slightly damped from salience
            var emotionalIntensity = nv.ViolationScore * 0.85;

            // Build what string with norm kind metadata
            var what = $"NormViolation:{nv.NormKind}|from={nv.Actor.Value}|score={nv.ViolationScore:F2}|audience={nv.HasAudience}";

            Encode(new EpisodicMemory(
                Guid.NewGuid(),
                nv.OccurredAt,
                what,
                Salience: salience,
                EmotionalTag.Negative,
                Strength: ComputeInitialStrength(salience, EmotionalTag.Negative),
                OtherPerson: nv.Actor == ctx.Id ? null : nv.Actor,
                PeakEmotion: EmotionalTag.Negative,
                EndEmotion: ctx.Snapshot.Psychology.DominantEmotion switch
                {
                    Psychology.DiscreteEmotion.Shame => EmotionalTag.Negative,
                    Psychology.DiscreteEmotion.Anger => EmotionalTag.Negative,
                    Psychology.DiscreteEmotion.Sadness => EmotionalTag.Negative,
                    Psychology.DiscreteEmotion.Fear => EmotionalTag.Negative,
                    _ => EmotionalTag.Neutral
                }),
                ctx, outbox);
        }

        /// <summary>
        /// Consolidates memories after sleep ends.
        /// <para>
        /// Neuroscientific basis: the REM phase of sleep reinforces episodic memories with high
        /// salience — experiences that were emotionally or situationally important.
        /// The implementation reinforces the 10 highest-salience episodes by <see cref="MemoryConfig.SleepConsolidationBoost"/>.
        /// </para>
        /// </summary>
        /// <param name="at">Time sleep ended.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Output queue of events.</param>
        private void ConsolidateMemories(WDateTime at, IHumanContext ctx, IEventCollector outbox)
        {
            var episodes = State.Episodes.ToList();

            // REM consolidation prioritises by salience weighted by emotional intensity.
            // Negative memories consolidate most strongly — negativity bias (McGaugh 2000).
            var toBoost = episodes
                .OrderByDescending(e =>
                {
                    var emotionBoost = e.Emotion switch
                    {
                        EmotionalTag.Negative => 0.35,
                        EmotionalTag.Positive => 0.20,
                        EmotionalTag.Mixed => 0.15,
                        _ => 0.0
                    };
                    return e.Salience + emotionBoost;
                })
                .Take(10)
                .Select(e => e with
                {
                    Strength = Math.Min(1.0, e.Strength + Config.SleepConsolidationBoost)
                })
                .ToList();

            // Merge back into the list (the Dictionary guarantees O(1) lookup per episode)
            var lookup = episodes.ToDictionary(e => e.Id);
            foreach (var boosted in toBoost)
            {
                lookup[boosted.Id] = boosted;
            }

            State = new MemoryIndex(lookup.Values.ToList()) { Knowledge = State.Knowledge };

            outbox.Add(new MemoryConsolidated(at, ctx.Id, toBoost.Count));

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultMemoryEngine)))
            {
                _log.MemoryConsolidated(ctx.Id.Value.ToString(), toBoost.Count);
            }
        }

        /// <summary>
        /// Proxy for chronic negative mood: the number of days since the most recent negative/mixed episode.
        /// Clamped to 30 days — longer periods carry a constant spiral risk.
        /// </summary>
        /// <param name="episodes">The episodic memories to evaluate.</param>
        /// <param name="now">Current game time.</param>
        /// <returns>The number of days (0–30) since the last negative/mixed episode.</returns>
        private static double ComputeDaysInNegativeMood(
            IReadOnlyList<EpisodicMemory> episodes, WDateTime now)
        {
            var recentNegative = episodes
                .Where(e => e.Emotion is EmotionalTag.Negative or EmotionalTag.Mixed)
                .OrderByDescending(e => e.When.WorldTicks)
                .FirstOrDefault();

            if (recentNegative is null) return 0.0;
            return Math.Min(30.0, (now - recentNegative.When).TotalDays);
        }

        // The initial Strength depends on the emotional intensity of the episode.
        // Negative memories are encoded more strongly (negativity bias — Baumeister et al. 2001).
        private static double ComputeInitialStrength(double salience, EmotionalTag emotion)
        {
            var intensity = emotion switch
            {
                EmotionalTag.Negative => 1.00,
                EmotionalTag.Positive => 0.85,
                EmotionalTag.Mixed => 0.65,
                _ => 0.45
            };
            return Math.Clamp(salience * intensity * 0.7 + 0.3, 0.3, 1.0);
        }

        private static double ComputePeakEndSalience(InteractionOutcome io)
        {
            if (io.PeakValence is null || io.EndValence is null)
                return io.Accepted ? 0.7 : 0.9;

            // Fredrickson & Kahneman 1993: salience = (|peak|×1.5 + |end|) / 2.5
            var raw = (Math.Abs(io.PeakValence.Value) * 1.5 + Math.Abs(io.EndValence.Value)) / 2.5;
            var negativityBoost = io.PeakValence.Value < 0 ? 0.08 : 0.0;
            return Math.Clamp(raw + negativityBoost, 0.3, 1.0);
        }

        private static EmotionalTag ValenceToEmotionalTag(double? v)
            => v switch
            {
                null => EmotionalTag.Neutral,
                > 0.2 => EmotionalTag.Positive,
                < -0.2 => EmotionalTag.Negative,
                _ => EmotionalTag.Mixed
            };

        private static double ComputeCognitiveBurden(IHumanContext ctx)
        {
            var stress = Math.Clamp(ctx.Snapshot.Psychology.Stress / 100.0, 0.0, 1.0);
            var fatigue = Math.Clamp(1.0 - ctx.Snapshot.Physiology.Energy / 100.0, 0.0, 1.0);
            var crowding = Math.Clamp(ctx.Snapshot.InteractionSurface.Crowding, 0.0, 1.0);
            return stress * 0.40 + fatigue * 0.35 + crowding * 0.25;
        }

        private static double SalienceForAction(string actionName, IHumanContext ctx)
            => actionName switch
            {
                InviteIntimacy => 0.9,  // Nejvyšší — intimní interakce jsou výrazné
                ReachOut => 0.6,  // Sociální kontakt
                Eat => 0.5,  // Biologická potřeba
                Sleep => 0.4,  // Rutina — nízká salience
                Drink => 0.3,  // Rutina
                _ => 0.5   // Výchozí pro neznámé akce
            };

        /// <summary>
        /// Assigns an emotion to an episode based on the action type or the character's current valence.
        /// If the action has no fixed emotion, the psychological valence from the snapshot decides.
        /// </summary>
        /// <param name="actionName">The action name.</param>
        /// <param name="valence">The character's current psychological valence (-1.0 to 1.0).</param>
        /// <returns>The emotional tag for the episode.</returns>
        private static EmotionalTag EmotionFor(string actionName, double valence)
            => actionName switch
            {
                InviteIntimacy or ReachOut or SelfCare => EmotionalTag.Positive,
                Flee or Fight => EmotionalTag.Negative,
                _ => valence switch
                {
                    > 0.05 => EmotionalTag.Positive,
                    < -0.05 => EmotionalTag.Negative,
                    _ => EmotionalTag.Neutral
                }
            };

        #endregion Privátní metody
    }
}
