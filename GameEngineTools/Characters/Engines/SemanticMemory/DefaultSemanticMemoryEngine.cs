// DefaultSemanticMemoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultSemanticMemoryEngine : ISemanticMemoryEngine
    {
        #region State and configuration

        public SemanticMemoryState State { get; private set; }

        public SemanticMemoryConfig Config { get; }

        #endregion

        #region Construction

        public DefaultSemanticMemoryEngine(IOptions<SemanticMemoryConfig> cfg)
        {
            Config = cfg.Value;
            State = SemanticMemoryState.Empty;
        }

        #endregion

        #region IEngine

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0.0, dt.TotalHours / 24.0);
            if (days <= 0.0 || State.People.Count == 0)
            {
                return;
            }

            var decayedPeople = new Dictionary<HumanId, PersonBeliefSet>();
            foreach (var (other, set) in State.People)
            {
                var beliefs = new Dictionary<PersonBeliefKind, PersonBelief>();
                foreach (var belief in set.Beliefs)
                {
                    var decay = Config.DecayPerDay * days * (1.0 - belief.Value.Stability * 0.8);
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

            var interpretations = BuildInterpretations(ctx, encoded, other.Value).ToList();
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
                var delta = interpretation.Weight * Config.LearningRate * resistance;
                var updated = current with
                {
                    Strength = Math.Clamp(current.Strength + (1.0 - current.Strength) * delta, 0.0, 1.0),
                    Stability = Math.Clamp(current.Stability + interpretation.Weight * Config.StabilityGainPerEvidence, 0.0, 0.95),
                    EvidenceCount = current.EvidenceCount + interpretation.SupportCount,
                    LastUpdatedAt = encoded.OccurredAt,
                    LastEvidenceSource = interpretation.Source
                };

                beliefs[interpretation.Kind] = updated;
                ApplyContradiction(encoded.OccurredAt, beliefs, interpretation.Kind, interpretation.Weight, interpretation.SupportCount);
                outbox.Add(new SemanticBeliefUpdated(encoded.OccurredAt, ctx.Id, other.Value, interpretation.Kind, updated.Strength, updated.EvidenceCount));
            }

            people[other.Value] = set with { Beliefs = beliefs };
            State = new SemanticMemoryState(people);
        }

        public void RestoreState(SemanticMemoryState state) => State = state;

        #endregion

        #region Private helpers

        private IEnumerable<BeliefInterpretation> BuildInterpretations(IHumanContext ctx, MemoryEncoded encoded, HumanId other)
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
            int supportCount)
        {
            foreach (var opposing in OpposingBeliefs(kind))
            {
                if (!beliefs.TryGetValue(opposing, out var current))
                {
                    continue;
                }

                var disconfirmation = weight * Config.ContradictionRate * (0.35 + current.Stability) * (1.0 + (supportCount - 1) * 0.25);
                beliefs[opposing] = current with
                {
                    Strength = Math.Max(0.0, current.Strength - disconfirmation),
                    Stability = Math.Max(0.0, current.Stability - Config.ContradictionStabilityHit * Math.Max(1, supportCount - 1)),
                    LastUpdatedAt = now,
                    LastEvidenceSource = $"disconfirmed-by:{kind}"
                };
            }
        }

        private static IEnumerable<InterpretedBeliefSignal> InterpretEpisode(SemanticEpisodeSample episode)
        {
            var perceived = episode.PerceivedWhat ?? episode.What ?? string.Empty;
            var text = $"{episode.What}|{perceived}";

            if (episode.DirectEvidence is { } direct)
            {
                yield return new InterpretedBeliefSignal(direct.Kind, Math.Clamp(direct.Weight, 0.0, 1.0), direct.Source);
            }

            if (text.Contains("accepted", StringComparison.OrdinalIgnoreCase))
            {
                yield return new InterpretedBeliefSignal(PersonBeliefKind.Warm, 0.20, "pattern-accepted");
                if (text.Contains("Validation", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("SelfDisclosure", StringComparison.OrdinalIgnoreCase)
                    || perceived.Contains("PerceivedWarmth:", StringComparison.Ordinal))
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.EmotionallySafe, 0.24, "pattern-safe");
                }

                if (text.Contains("Invite", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("repair", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("help", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new InterpretedBeliefSignal(PersonBeliefKind.Reliable, 0.22, "pattern-follow-through");
                }
            }

            if (text.Contains("declined", StringComparison.OrdinalIgnoreCase) || perceived.Contains("PerceivedThreat:", StringComparison.Ordinal))
            {
                yield return new InterpretedBeliefSignal(PersonBeliefKind.Rejecting, 0.24, "pattern-rejection");
            }

            if (text.Contains("cold", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ignore", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Critical", StringComparison.OrdinalIgnoreCase)
                || perceived.Contains("PerceivedSlight:", StringComparison.Ordinal))
            {
                yield return new InterpretedBeliefSignal(PersonBeliefKind.Critical, 0.18, "pattern-critical");
            }

            if (text.Contains("help", StringComparison.OrdinalIgnoreCase)
                || text.Contains("repair-accepted", StringComparison.OrdinalIgnoreCase)
                || text.Contains("interaction-received", StringComparison.OrdinalIgnoreCase))
            {
                yield return new InterpretedBeliefSignal(PersonBeliefKind.Reliable, 0.18, "pattern-reliable");
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

        #endregion
    }
}
