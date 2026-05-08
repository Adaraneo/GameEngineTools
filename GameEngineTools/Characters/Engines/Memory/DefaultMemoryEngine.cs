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
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static ActionNames;

    /// <summary>
    /// Výchozí implementace paměťového enginu.
    ///
    /// Implementuje tři kognitivně-realistické principy:
    /// <list type="bullet">
    ///   <item><b>Ebbinghausova křivka zapomínání</b> — exponenciální pokles síly paměti,
    ///         nikoli lineární. Čerstvé paměti se rozpadají rychleji než dobře zakotvené.</item>
    ///   <item><b>Konsolidace navázaná na spánek</b> — posílení nejsalientnějších epizod se
    ///         triggeruje událostí <see cref="SleepEvents.SleepEnded"/>, nikoli uplynutím 24h.</item>
    ///   <item><b>Reinforcement (spacing effect)</b> — opakovaný zážitek stejného druhu nekreuje
    ///         duplicitní záznamy, ale posiluje stávající epizodu a aktualizuje její timestamp.</item>
    /// </list>
    /// </summary>
    internal sealed class DefaultMemoryEngine : IMemoryEngine
    {
        #region Stav a konfigurace

        /// <summary>Aktuální stav paměti — seznam epizod.</summary>
        public MemoryIndex State { get; private set; }

        /// <summary>Konfigurace enginu (míra zapomínání, boost, práh prořezání).</summary>
        public MemoryConfig Config { get; }

        #endregion Stav a konfigurace

        #region Privátní pole

        private readonly ILogger _log;
        private readonly IMemoryFidelityPolicy? _memoryFidelityPolicy;

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Vytvoří instanci <see cref="DefaultMemoryEngine"/>.
        /// </summary>
        /// <param name="cfg">Konfigurace injektovaná přes Options pattern.</param>
        /// <param name="loggerFactory">Továrna na logger — umožňuje scope per postava.</param>
        public DefaultMemoryEngine(
            IOptions<MemoryConfig> cfg,
            ILoggerFactory loggerFactory,
            IMemoryFidelityPolicy? memoryFidelityPolicy = null)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultMemoryEngine>();
            _memoryFidelityPolicy = memoryFidelityPolicy;

            // Inicializuj prázdný stav — žádné vzpomínky
            State = new MemoryIndex(
                new List<EpisodicMemory>());
        }

        #endregion Konstruktor

        #region Veřejné API

        /// <summary>
        /// Zakóduje novou epizodu do paměti.
        ///
        /// Pokud epizoda se stejným klíčem <c>Kind</c> již existuje a je stále silná
        /// (nad prahem prořezání), aplikuje <b>reinforcement</b> — posílí stávající záznam
        /// a aktualizuje jeho timestamp. Tím se modeluje spacing effect:
        /// opakovaný zážitek upevňuje paměť, místo aby plodil duplicity.
        /// </summary>
        /// <param name="episode">Epizoda k zakódování.</param>
        /// <param name="ctx">Kontext postavy (ID, snapshot).</param>
        /// <param name="outbox">Výstupní fronta doménových událostí.</param>
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
                // Nehledáme shodu podle syrového Kind stringu,
                // ale podle explicitního reinforcement klíče.
                var incomingKey = MemoryReinforcementKeyBuilder.From(episode);

                var existingIndex = episodes.FindIndex(e =>
                    e.Strength >= Config.PruneThreshold
                    && MemoryReinforcementKeyBuilder.From(e) == incomingKey);

                if (existingIndex >= 0)
                {
                    // Posilujeme stávající paměť místo vytváření nového záznamu.
                    // Logika: opakovaný zážitek upevňuje stopu, ale neclampuje nad 1.0.
                    var existing = episodes[existingIndex];
                    var reinforced = existing with
                    {
                        Strength = Math.Min(1.0, existing.Strength + Config.ReinforcementBoost),

                        // Aktualizuj timestamp - "naposledy se to stalo teď"
                        When = episode.When,

                        // Udržuj poslední reprezentaci raw Kind / PercievedWhat.
                        // Díky explicitnímu reinforcement klíči už Kind nemusí být identita.
                        What = episode.What,
                        PerceivedWhat = episode.PerceivedWhat ?? existing.PerceivedWhat,

                        Distortion = Math.Max(existing.Distortion, episode.Distortion),
                        RecallConfidence = Math.Min(existing.RecallConfidence, episode.RecallConfidence)
                    };

                    episodes[existingIndex] = reinforced;
                    State = new MemoryIndex(episodes) { Knowledge = State.Knowledge };

                    _log.MemoryEncoded(
                        ctx.Id.Value.ToString(),
                        episode.What,
                        reinforced.Strength,
                        reinforced.Emotion.ToString());

                    // Vyzvaň událost i pro reinforcement — Strength je aktualizovaná
                    outbox.Add(new MemoryEncoded(episode.When, ctx.Id, existing.Id, reinforced.Strength, episode.What, reinforced.PerceivedWhat, reinforced.OtherPerson, reinforced.BeliefEvidence));
                    return;
                }

                // --- NOVÁ EPIZODA ---
                var encoded = episode with
                {
                    PerceivedWhat = episode.PerceivedWhat ?? BuildPerceivedWhat(episode, ctx),
                    Distortion = Math.Max(0.0, episode.Distortion + ComputeDistortion(ctx, episode)),
                    RecallConfidence = Math.Clamp(episode.RecallConfidence - ComputeDistortion(ctx, episode) * 0.5, 0.2, 1.0)
                };

                episodes.Add(encoded);
                State = new MemoryIndex(episodes) { Knowledge = State.Knowledge };

                _log.MemoryEncoded(
                    ctx.Id.Value.ToString(),
                    encoded.What,
                    encoded.Salience,
                    encoded.Emotion.ToString());

                outbox.Add(new MemoryEncoded(encoded.When, ctx.Id, encoded.Id, encoded.Strength, encoded.What, encoded.PerceivedWhat, encoded.OtherPerson, encoded.BeliefEvidence));
            }
        }

        /// <summary>
        /// Vrátí vzpomínky splňující zadaný predikát.
        /// Používá se z BehaviorEngine pro ovlivnění rozhodování postavy.
        /// </summary>
        /// <param name="predicate">Filtrační podmínka.</param>
        /// <returns>Filtrovaný seznam epizod (read-only snapshot).</returns>
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
                CognitiveBurden      = burden,
                CurrentValence       = ctx.Snapshot.Psychology.Valence,
                NeuroticismScore     = ctx.Personality.BigFive.Neuroticism,
                DaysInNegativeMood   = ComputeDaysInNegativeMood(ctx.Snapshot.Memory.Episodes, now)
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
        /// Reaguje na doménové události z ostatních enginů a kóduje je jako epizodické vzpomínky.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Schema <c>Kind</c>:</b> každý event se překládá do deterministického sémantického klíče
        /// přes <see cref="MemoryWhatParser"/>. Formát: <c>{Kategorie}:{Typ}:{Výsledek}|{klíč}={hodnota}</c>
        /// </para>
        /// <para>
        /// <b>Proč deterministický klíč?</b>
        /// <see cref="Encode"/> používá <c>Kind</c> jako klíč pro reinforcement (spacing effect) —
        /// opakovaný zážitek stejného typu posílí existující vzpomínku místo vytvoření nové.
        /// Kdyby byl klíč pokaždé jiný (např. obsahoval timestamp), reinforcement by nefungoval.
        /// </para>
        /// <para>
        /// <b>Zakódované typy událostí:</b>
        /// <list type="bullet">
        ///   <item><see cref="ActionCommitted"/> — každá provedená akce, salience dle důležitosti.</item>
        ///   <item><see cref="InteractionOutcome"/> — přijetí/odmítnutí interakce mezi postavami.</item>
        ///   <item><see cref="FirstImpressionFormed"/> — první setkání s novou postavou.</item>
        ///   <item><see cref="MicroPositive"/> / <see cref="MicroNegative"/> — mikrointerakce.</item>
        ///   <item><see cref="RepairAttempt"/> — pokus o smíření.</item>
        ///   <item><see cref="NightmareTriggered"/> — noční můra (vysoká salience, negativní emoce).</item>
        ///   <item><see cref="SleepEnded"/> — triggeruje konsolidaci paměti.</item>
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
                        // Vlastní akce — nejjednodušší schema, žádní aktéři
                        var what = MemoryWhatParser.Action(ac.ActionName);
                        var other = ac.TargetHuman == ctx.Id ? null : ac.TargetHuman;
                        var acSalience = SalienceForAction(ac.ActionName, ctx);
                        var acEmotion  = EmotionFor(ac.ActionName, ctx.Snapshot.Psychology.Valence);

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
                        // Schema zachytí: typ aktu, výsledek, oba aktéři
                        // Salience se počítá peak-end vzorcem (Fredrickson & Kahneman 1993)
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

                        // ToM: record knowledge that the other party performed a SelfDisclosure
                        if (io.Act == SpeechAct.SelfDisclosure && io.Accepted)
                        {
                            var otherId = io.From == ctx.Id ? io.To : io.From;
                            RecordKnowledge(otherId, ctx.Id, "SelfDisclosure", FactSource.DirectWitness, io.OccurredAt);
                        }
                        break;
                    }

                // ── První dojem ───────────────────────────────────────────────────────────
                case FirstImpressionFormed fi:
                    {
                        // První setkání — vždy vysoká salience, emoce závisí na Like
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
                        // Negativní mikrointerakce — o něco vyšší salience než pozitivní
                        // (negativní bias: nepříjemné věci si pamatujeme lépe)
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

                // ── Smíření ───────────────────────────────────────────────────────────────
                case RepairAttempt ra:
                    {
                        // Smíření nebo odmítnutí smíru — oba jsou vztahově zlomové momenty
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

                // ── Sexuální setkání ──────────────────────────────────────────────────────
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
                            ThirdPartyObservationType.Betrayal    => "Betrayal",
                            ThirdPartyObservationType.NegativeAct => "NegativeAct",
                            ThirdPartyObservationType.PositiveAct => "PositiveAct",
                            _                                     => "Unknown"
                        };
                        RecordKnowledge(tpa.Actor, tpa.Target, actionKind, FactSource.Gossip, tpa.OccurredAt);
                        break;
                    }

                case NightmareTriggered nt:
                    {
                        // Noční můra — nejvyšší salience ze spánkových událostí
                        // Postava si ji jasně pamatuje, zvyšuje stres i příští den
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

                            // Memory reconsolidation (Nader et al. 2000): každý recall
                            // driftuje emoci vzpomínky směrem k aktuální náladě.
                            // Negativní vzpomínky driftují 1.3× rychleji.
                            var currentValence = ctx.Snapshot.Psychology.Valence;
                            var driftRate = Config.ReconsolidationDriftRate
                                            * (ep.Emotion == EmotionalTag.Negative ? 1.3 : 1.0);
                            var numericEmotion = ep.Emotion switch
                            {
                                EmotionalTag.Positive =>  1.0,
                                EmotionalTag.Negative => -1.0,
                                EmotionalTag.Mixed    => -0.3,
                                _                     =>  0.0
                            };
                            var drifted = numericEmotion + (currentValence - numericEmotion) * driftRate;
                            var newEmotion = drifted switch
                            {
                                > 0.35  => EmotionalTag.Positive,
                                < -0.35 => EmotionalTag.Negative,
                                < 0.0   => EmotionalTag.Mixed,
                                _       => EmotionalTag.Neutral
                            };

                            episodes[idx] = ep with
                            {
                                RecallConfidence = Math.Clamp(ep.RecallConfidence + 0.03, 0.0, 1.0),
                                Emotion = newEmotion
                            };
                            State = new MemoryIndex(episodes) { Knowledge = State.Knowledge };
                        }
                        break;
                    }

                // ── Konsolidace po spánku ─────────────────────────────────────────────────
                // SleepEnded netriggeruje kódování nové vzpomínky — konsoliduje existující.
                // Viz ConsolidateMemories() — posílí top-N epizod dle salience.
                case SleepEnded se:
                    ConsolidateMemories(se.OccurredAt, ctx, outbox);
                    break;
            }
        }

        #endregion Handle — zpracování doménových událostí

        #region Tick — zapomínání (Ebbinghausova křivka)

        /// <summary>
        /// Volá se každý herní tick. Aplikuje exponenciální pokles síly paměti
        /// a prořezává vzpomínky pod prahem.
        ///
        /// <b>Proč exponenciální, ne lineární?</b>
        /// Ebbinghausova křivka zapomínání ukazuje, že paměti se rozpadají
        /// nejrychleji krátce po zakódování a pak stále pomaleji. Exponenciální
        /// funkce <c>e^(-k*t)</c> toto chování přesně modeluje:
        /// silná paměť (Strength=1.0) se rozpadá pomalu,
        /// slabá (Strength=0.1) zmizí rychle.
        /// </summary>
        /// <param name="now">Aktuální herní čas.</param>
        /// <param name="dt">Délka ticku.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Výstupní fronta událostí.</param>
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

            // Prořezání — odstraní epizody pod prahem, aby paměť nerostla donekonečna
            episodes = episodes
                .Where(e => e.Strength >= Config.PruneThreshold)
                .ToList();

            // Preserve existing Knowledge facts when rebuilding episodic state
            State = new MemoryIndex(episodes) { Knowledge = State.Knowledge };

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
        }

        #endregion Tick — zapomínání (Ebbinghausova křivka)

        #region Obnovení stavu

        /// <summary>
        /// Obnoví stav enginu ze snapshotu (např. při načítání uložené hry).
        /// </summary>
        /// <param name="state">Předchozí stav paměti.</param>
        public void RestoreState(MemoryIndex state) => State = state;

        #endregion Obnovení stavu

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
            FactSource source, WDateTime now)
        {
            var confidence = source == FactSource.DirectWitness
                ? Config.DirectWitnessConfidence
                : Config.GossipConfidence;

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
                    LearnedAt  = now   // refresh timestamp
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
                    Confidence: confidence));
            }
            State = State with { Knowledge = updated };
        }

        /// <summary>
        /// Konsoliduje paměti po skončení spánku.
        ///
        /// Neurovědní základ: REM fáze spánku posiluje epizodické paměti s vysokou
        /// salience — tedy zážitky, které byly emocionálně nebo situačně důležité.
        /// Implementace posiluje 10 epizod s nejvyšší salience o <see cref="MemoryConfig.SleepConsolidationBoost"/>.
        /// </summary>
        /// <param name="at">Čas konce spánku.</param>
        /// <param name="ctx">Kontext postavy.</param>
        /// <param name="outbox">Výstupní fronta událostí.</param>
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

            // Merge zpět do seznamu (Dictionary zaručí O(1) lookup per epizoda)
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
        /// Určí salience (důležitost) epizody na základě typu akce.
        /// Intimní a sociální akce mají vyšší salience — jsou emocionálně výraznější.
        /// </summary>
        /// <param name="actionName">Název akce z <see cref="ActionNames"/>.</param>
        /// <param name="ctx">Kontext postavy (pro budoucí rozšíření).</param>
        /// <returns>Hodnota salience v rozsahu 0.0–1.0.</returns>
        // Proxy pro chronickou negativní náladu: počet dní od nejnovější negativní/mixed epizody.
        // Clamped na 30 dní — delší periody mají konstantní spirálové riziko.
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

        // Počáteční Strength závisí na emocionální intenzitě epizody.
        // Negativní vzpomínky se kódují silněji (negativity bias — Baumeister et al. 2001).
        private static double ComputeInitialStrength(double salience, EmotionalTag emotion)
        {
            var intensity = emotion switch
            {
                EmotionalTag.Negative => 1.00,
                EmotionalTag.Positive => 0.85,
                EmotionalTag.Mixed    => 0.65,
                _                     => 0.45
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
            => v switch { null => EmotionalTag.Neutral, > 0.2 => EmotionalTag.Positive,
                          < -0.2 => EmotionalTag.Negative, _ => EmotionalTag.Mixed };

        private static double ComputeCognitiveBurden(IHumanContext ctx)
        {
            var stress   = Math.Clamp(ctx.Snapshot.Psychology.Stress / 100.0, 0.0, 1.0);
            var fatigue  = Math.Clamp(1.0 - ctx.Snapshot.Physiology.Energy / 100.0, 0.0, 1.0);
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
        /// Přiřadí emoci epizodě na základě typu akce nebo aktuální valence postavy.
        /// Pokud akce nemá pevnou emoci, rozhoduje psychologická valence ze snapshotu.
        /// </summary>
        /// <param name="actionName">Název akce.</param>
        /// <param name="valence">Aktuální psychologická valence postavy (-1.0 až 1.0).</param>
        /// <returns>Emocionální tag pro epizodu.</returns>
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
