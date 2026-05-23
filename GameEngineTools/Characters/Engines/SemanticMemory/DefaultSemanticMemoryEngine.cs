// DefaultSemanticMemoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.Memory.MemoryWhatParser;

    internal sealed class DefaultSemanticMemoryEngine : ISemanticMemoryEngine
    {
        #region State and configuration

        public SemanticMemoryState State { get; private set; }

        public SemanticMemoryConfig Config { get; }

        #endregion State and configuration

        #region Private fields

        private readonly ILogger _log;

        #endregion Private fields

        #region Construction

        public DefaultSemanticMemoryEngine(IOptions<SemanticMemoryConfig> cfg, ILoggerFactory? loggerFactory = null)
        {
            Config = cfg.Value;
            _log = (loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
                .CreateLogger<DefaultSemanticMemoryEngine>();
            State = SemanticMemoryState.Empty;
        }

        #endregion Construction

        #region IEngine

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0.0, dt.TotalHours / 24.0);
            if (days <= 0.0 || State.People.Count == 0)
            {
                return;
            }

            var edges = ctx.Snapshot.Relationships.Edges;

            var decayedPeople = new Dictionary<HumanId, PersonBeliefSet>();
            foreach (var (other, set) in State.People)
            {
                // ── Navarro 8× gap rule (Navarro et al. 2017) ──────────────────────────
                // Pokud uplynulo déle než 8× průměrný meziinterakční interval,
                // decay se znásobí NavarroDecayAccelerator (default 3×).
                var closeness = edges.TryGetValue(other, out var edge) ? edge.Closeness : 30.0;
                var expectedIntervalDays = closeness > 70.0 ? 3.0
                    : closeness > 40.0 ? 7.0
                    : 21.0;
                var navarroThreshold = expectedIntervalDays * Config.NavarroCriticalMultiple;

                var oldestBelief = set.Beliefs.Values
                    .OrderBy(b => b.LastUpdatedAt.WorldTicks)
                    .FirstOrDefault();
                var daysSinceContact = oldestBelief is not null
                    ? Math.Max(0.0, (now - oldestBelief.LastUpdatedAt).TotalDays)
                    : 0.0;

                var gapMultiplier = daysSinceContact > navarroThreshold
                    ? Config.NavarroDecayAccelerator
                    : 1.0;
                // ──────────────────────────────────────────────────────────────────────

                var beliefs = new Dictionary<PersonBeliefKind, PersonBelief>();
                foreach (var belief in set.Beliefs)
                {
                    var decay = Config.DecayPerDay * days * (1.0 - belief.Value.Stability * 0.8) * gapMultiplier;
                    var strength = Math.Max(0.0, belief.Value.Strength - decay);
                    beliefs[belief.Key] = belief.Value with { Strength = strength };
                }

                decayedPeople[other] = set with { Beliefs = beliefs };
            }

            State = new SemanticMemoryState(decayedPeople);
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is not MemoryEncoded encoded)
            {
                return;
            }

            var other = encoded.OtherPerson ?? encoded.BeliefEvidence?.Other;
            if (other is null)
            {
                return;
            }

            // ── Attachment style modulation (Bartholomew-Horowitz 2D model) ──────────
            var (learningMult, contradictionMult, safeDiscount) =
                ComputeAttachmentMultipliers(ctx.Personality.Attachment);
            // ─────────────────────────────────────────────────────────────────────────

            var interpretations = BuildInterpretations(ctx, encoded, other.Value, safeDiscount).ToList();
            if (interpretations.Count == 0)
            {
                return;
            }

            var people = State.People.ToDictionary(
                pair => pair.Key,
                pair => new PersonBeliefSet(pair.Value.Other, pair.Value.Beliefs.ToDictionary(entry => entry.Key, entry => entry.Value)));

            if (!people.TryGetValue(other.Value, out var set))
            {
                set = new PersonBeliefSet(other.Value, new Dictionary<PersonBeliefKind, PersonBelief>());
                people[other.Value] = set;
            }

            var beliefs = set.Beliefs.ToDictionary(entry => entry.Key, entry => entry.Value);

            foreach (var interpretation in interpretations)
            {
                var current = beliefs.TryGetValue(interpretation.Kind, out var existing)
                    ? existing
                    : new PersonBelief(other.Value, interpretation.Kind, 0.0, 0.0, 0, encoded.OccurredAt);

                var resistance = 1.0 - current.Stability * 0.6;
                var delta = interpretation.Weight * Config.LearningRate * learningMult * resistance;
                var updated = current with
                {
                    Strength = Math.Clamp(current.Strength + (1.0 - current.Strength) * delta, 0.0, 1.0),
                    Stability = Math.Clamp(current.Stability + interpretation.Weight * Config.StabilityGainPerEvidence, 0.0, 0.95),
                    EvidenceCount = current.EvidenceCount + interpretation.SupportCount,
                    LastUpdatedAt = encoded.OccurredAt,
                    LastEvidenceSource = interpretation.Source
                };

                beliefs[interpretation.Kind] = updated;
                ApplyContradiction(encoded.OccurredAt, beliefs, interpretation.Kind, interpretation.Weight, interpretation.SupportCount, contradictionMult);
                outbox.Add(new SemanticBeliefUpdated(encoded.OccurredAt, ctx.Id, other.Value, interpretation.Kind, updated.Strength, updated.EvidenceCount));

                if (Math.Abs(updated.Strength - current.Strength) > 0.1)
                {
                    using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultSemanticMemoryEngine), relatedPersonId: other.Value.Value))
                    {
                        _log.BeliefUpdated(
                            ctx.Id.Value.ToString(),
                            other.Value.Value.ToString(),
                            interpretation.Kind.ToString(),
                            current.Strength,
                            updated.Strength,
                            updated.EvidenceCount);
                    }
                }
            }

            people[other.Value] = set with { Beliefs = beliefs };
            State = new SemanticMemoryState(people);
        }

        public void RestoreState(SemanticMemoryState state) => State = state;

        #endregion IEngine

        #region Diagnostics & management

        /// <inheritdoc/>
        public IReadOnlyList<PersonBelief> GetBeliefsSorted(HumanId other)
        {
            var set = State.GetBeliefs(other);
            if (set is null) return Array.Empty<PersonBelief>();
            return set.Beliefs.Values
                .OrderByDescending(b => b.Strength)
                .ThenBy(b => b.Kind.ToString())
                .ToList();
        }

        /// <inheritdoc/>
        public void ForgetPerson(HumanId other)
        {
            if (!State.People.ContainsKey(other)) return;
            var updated = State.People
                .Where(kv => kv.Key != other)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            State = new SemanticMemoryState(updated);
        }

        #endregion Diagnostics & management

        #region Private helpers

        private IEnumerable<BeliefInterpretation> BuildInterpretations(
            IHumanContext ctx, MemoryEncoded encoded, HumanId other, double safeDiscount)
        {
            var recentEpisodes = ctx.Snapshot.Memory.Episodes
                .Where(e => e.OtherPerson == other)
                .OrderByDescending(e => e.When)
                .Take(Config.PatternWindowSize - 1)
                .ToList();

            var window = new List<SemanticEpisodeSample>(recentEpisodes.Count + 1);
            window.AddRange(recentEpisodes.Select(ToSample));
            window.Add(ToSample(encoded));

            foreach (var group in window
                .SelectMany(InterpretEpisode)
                .GroupBy(x => x.Kind))
            {
                var supportCount = group.Count();
                var directSupport = encoded.BeliefEvidence?.Kind == group.Key ? encoded.BeliefEvidence.Weight : 0.0;
                var averageWeight = group.Average(x => x.Weight);
                var aggregateWeight = averageWeight * (supportCount >= Config.MinimumPatternSupport ? 1.0 : 0.45);
                aggregateWeight += directSupport * 0.35;
                aggregateWeight = Math.Clamp(aggregateWeight, 0.0, 1.0);

                // Avoidant attachment potlačuje EmotionallySafe belief
                if (group.Key == PersonBeliefKind.EmotionallySafe)
                {
                    aggregateWeight *= safeDiscount;
                }

                if (aggregateWeight < 0.08)
                {
                    continue;
                }

                yield return new BeliefInterpretation(group.Key, aggregateWeight, supportCount, group.Last().Source);
            }
        }

        private void ApplyContradiction(
            WDateTime now,
            IDictionary<PersonBeliefKind, PersonBelief> beliefs,
            PersonBeliefKind kind,
            double weight,
            int supportCount,
            double contradictionMult = 1.0)
        {
            foreach (var opposing in OpposingBeliefs(kind))
            {
                if (!beliefs.TryGetValue(opposing, out var current))
                {
                    continue;
                }

                var disconfirmation = weight * Config.ContradictionRate * contradictionMult
                    * (0.35 + current.Stability)
                    * (1.0 + (supportCount - 1) * 0.25);
                beliefs[opposing] = current with
                {
                    Strength = Math.Max(0.0, current.Strength - disconfirmation),
                    Stability = Math.Max(0.0, current.Stability - Config.ContradictionStabilityHit * Math.Max(1, supportCount - 1)),
                    LastUpdatedAt = now,
                    LastEvidenceSource = $"disconfirmed-by:{kind}"
                };
            }
        }

        /// <summary>
        /// Mapuje AttachmentProfile (2D kontinuální model) na learning/contradiction/safeDiscount multiplikátory.
        /// Anxiety  → hyperaktivace (rychlejší učení, vyšší contradikce)
        /// Avoidance → deaktivace (pomalejší učení, potlačení EmotionallySafe)
        /// Kombinace obou (Fearful) → nestabilní profil
        /// </summary>
        private (double learningMult, double contradictionMult, double safeDiscount)
            ComputeAttachmentMultipliers(AttachmentProfile profile)
        {
            // Anxiety drives hyperactivation of learning and contradiction sensitivity
            var learningMult = 1.0
                + profile.Anxiety * (Config.AttachmentLearningBoostAnxious - 1.0)
                - profile.Avoidance * (1.0 - Config.AttachmentLearningDiscountAvoidant);

            // The Fearful (high Anxiety × high Avoidance) interaction term uses
            // (BoostDisorganized - 1.0) rather than the delta from Anxious, so that
            // the combined Fearful profile (where EmotionallySafe is suppressed by avoidance)
            // still produces higher total contradiction impact than Secure.
            var contradictionMult = 1.0
                + profile.Anxiety * (Config.AttachmentContradictionBoostAnxious - 1.0)
                + profile.Anxiety * profile.Avoidance * (Config.AttachmentContradictionBoostDisorganized - 1.0);

            // Avoidance suppresses EmotionallySafe encoding (deactivation strategy)
            var safeDiscount = 1.0 - profile.Avoidance * (1.0 - Config.AttachmentSafeDiscountAvoidant);

            return (
                Math.Clamp(learningMult, 0.5, 2.0),
                Math.Clamp(contradictionMult, 0.5, 2.5),
                Math.Clamp(safeDiscount, 0.5, 1.0));
        }

        private static IEnumerable<InterpretedBeliefSignal> InterpretEpisode(SemanticEpisodeSample episode)
        {
            var descriptor = MemoryWhatParser.ParseDescriptor(episode.What, episode.PerceivedWhat);

            if (episode.DirectEvidence is { } direct)
            {
                yield return new InterpretedBeliefSignal(
                    direct.Kind,
                    Math.Clamp(direct.Weight, 0.0, 1.0),
                    direct.Source);
            }

            if (descriptor.Category == "Interaction")
            {
                var accepted = string.Equals(descriptor.Outcome, "Accepted", StringComparison.OrdinalIgnoreCase);
                var rejected = string.Equals(descriptor.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase);

                if (accepted)
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.Warm, 0.20, "pattern-accepted");

                    if (descriptor.Type is "Validation" or "SelfDisclosure" or "Meta"
                        || descriptor.PerceivedTone == PerceivedMemoryTone.Warm)
                    {
                        yield return new InterpretedBeliefSignal(PersonBeliefKind.EmotionallySafe, 0.24, "pattern-safe");
                    }

                    if (descriptor.Type is "Invite"
                        || descriptor.Parameters.ContainsKey("repair"))
                    {
                        yield return new InterpretedBeliefSignal(PersonBeliefKind.Reliable, 0.22, "pattern-follow-through");
                    }
                }

                if (rejected || descriptor.PerceivedTone == PerceivedMemoryTone.Threat)
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.Rejecting, 0.24, "pattern-rejection");
                }
            }

            if (descriptor.Category == "Relation" && descriptor.Type == "MicroNegative")
            {
                var microKind = MemoryWhatParser.GetMicroEventKind(descriptor);

                if (descriptor.PerceivedTone == PerceivedMemoryTone.Slight)
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.Critical, 0.18, "pattern-critical");
                }

                if (microKind is MemoryMicroEventKinds.Ignore
                    or MemoryMicroEventKinds.Cold
                    or MemoryMicroEventKinds.Criticism
                    or MemoryMicroEventKinds.Dismissal
                    or MemoryMicroEventKinds.Slight)
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.Critical, 0.18, "pattern-critical");
                }
            }

            if (descriptor.Category == "Relation"
                && descriptor.Type == "Repair"
                && string.Equals(descriptor.Outcome, "Accepted", StringComparison.OrdinalIgnoreCase))
            {
                yield return new InterpretedBeliefSignal(PersonBeliefKind.Reliable, 0.20, "repair-accepted");
            }

            if (descriptor.Category == "Relation"
                && descriptor.Type == "MicroPositive")
            {
                var microKind = MemoryWhatParser.GetMicroEventKind(descriptor);

                if (microKind is MemoryMicroEventKinds.Help
                    or MemoryMicroEventKinds.Support
                    or MemoryMicroEventKinds.Repair)
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.Reliable, 0.18, "pattern-reliable");
                }

                if (microKind is MemoryMicroEventKinds.Warmth
                    or MemoryMicroEventKinds.Validation)
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.EmotionallySafe, 0.18, "pattern-safe");
                }
            }
        }

        private static SemanticEpisodeSample ToSample(EpisodicMemory episode)
            => new(episode.What, episode.PerceivedWhat, episode.BeliefEvidence);

        private static SemanticEpisodeSample ToSample(MemoryEncoded episode)
            => new(episode.What, episode.PerceivedWhat, episode.BeliefEvidence);

        private static IEnumerable<PersonBeliefKind> OpposingBeliefs(PersonBeliefKind kind)
            => kind switch
            {
                PersonBeliefKind.Rejecting => new[] { PersonBeliefKind.EmotionallySafe, PersonBeliefKind.Warm },
                PersonBeliefKind.EmotionallySafe => new[] { PersonBeliefKind.Rejecting, PersonBeliefKind.Critical },
                PersonBeliefKind.Reliable => new[] { PersonBeliefKind.Rejecting, PersonBeliefKind.Critical },
                PersonBeliefKind.Warm => new[] { PersonBeliefKind.Critical, PersonBeliefKind.Rejecting },
                PersonBeliefKind.Critical => new[] { PersonBeliefKind.Warm, PersonBeliefKind.EmotionallySafe },
                _ => Array.Empty<PersonBeliefKind>()
            };

        private sealed record SemanticEpisodeSample(
            string? What,
            string? PerceivedWhat,
            PersonBeliefEvidence? DirectEvidence);

        private sealed record InterpretedBeliefSignal(
            PersonBeliefKind Kind,
            double Weight,
            string Source);

        private sealed record BeliefInterpretation(
            PersonBeliefKind Kind,
            double Weight,
            int SupportCount,
            string Source);

        #endregion Private helpers
    }
}
