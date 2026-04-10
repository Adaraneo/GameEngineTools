// DefaultSemanticMemoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultSemanticMemoryEngine : ISemanticMemoryEngine
    {
        public SemanticMemoryState State { get; private set; }
        public SemanticMemoryConfig Config { get; }

        public DefaultSemanticMemoryEngine(IOptions<SemanticMemoryConfig> cfg)
        {
            Config = cfg.Value;
            State = SemanticMemoryState.Empty;
        }

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
            if (@event is not MemoryEncoded { BeliefEvidence: { } evidence } encoded)
            {
                return;
            }

            var people = State.People.ToDictionary(
                pair => pair.Key,
                pair => new PersonBeliefSet(pair.Value.Other, pair.Value.Beliefs.ToDictionary(entry => entry.Key, entry => entry.Value)));

            if (!people.TryGetValue(evidence.Other, out var set))
            {
                set = new PersonBeliefSet(evidence.Other, new Dictionary<PersonBeliefKind, PersonBelief>());
                people[evidence.Other] = set;
            }

            var beliefs = set.Beliefs.ToDictionary(entry => entry.Key, entry => entry.Value);
            var current = beliefs.TryGetValue(evidence.Kind, out var existing)
                ? existing
                : new PersonBelief(evidence.Other, evidence.Kind, 0.0, 0.0, 0, encoded.OccurredAt);

            var resistance = 1.0 - current.Stability * 0.6;
            var delta = evidence.Weight * Config.LearningRate * resistance;
            var updated = current with
            {
                Strength = Math.Clamp(current.Strength + (1.0 - current.Strength) * delta, 0.0, 1.0),
                Stability = Math.Clamp(current.Stability + evidence.Weight * Config.StabilityGainPerEvidence, 0.0, 0.95),
                EvidenceCount = current.EvidenceCount + 1,
                LastUpdatedAt = encoded.OccurredAt,
                LastEvidenceSource = evidence.Source
            };

            beliefs[evidence.Kind] = updated;
            ApplyContradiction(encoded.OccurredAt, beliefs, evidence.Other, evidence.Kind, evidence.Weight);

            people[evidence.Other] = set with { Beliefs = beliefs };
            State = new SemanticMemoryState(people);
            outbox.Add(new SemanticBeliefUpdated(encoded.OccurredAt, ctx.Id, evidence.Other, evidence.Kind, updated.Strength, updated.EvidenceCount));
        }

        public void RestoreState(SemanticMemoryState state) => State = state;

        private void ApplyContradiction(
            WDateTime now,
            IDictionary<PersonBeliefKind, PersonBelief> beliefs,
            HumanId other,
            PersonBeliefKind kind,
            double weight)
        {
            foreach (var opposing in OpposingBeliefs(kind))
            {
                if (!beliefs.TryGetValue(opposing, out var current))
                {
                    continue;
                }

                var reduction = weight * Config.ContradictionRate * (0.35 + current.Stability);
                beliefs[opposing] = current with
                {
                    Strength = Math.Max(0.0, current.Strength - reduction),
                    LastUpdatedAt = now
                };
            }
        }

        private static IEnumerable<PersonBeliefKind> OpposingBeliefs(PersonBeliefKind kind)
            => kind switch
            {
                PersonBeliefKind.Rejecting => new[] { PersonBeliefKind.EmotionallySafe, PersonBeliefKind.Warm },
                PersonBeliefKind.EmotionallySafe => new[] { PersonBeliefKind.Rejecting, PersonBeliefKind.Critical },
                PersonBeliefKind.Reliable => Array.Empty<PersonBeliefKind>(),
                PersonBeliefKind.Warm => new[] { PersonBeliefKind.Critical, PersonBeliefKind.Rejecting },
                PersonBeliefKind.Critical => new[] { PersonBeliefKind.Warm, PersonBeliefKind.EmotionallySafe },
                _ => Array.Empty<PersonBeliefKind>()
            };
    }
}
