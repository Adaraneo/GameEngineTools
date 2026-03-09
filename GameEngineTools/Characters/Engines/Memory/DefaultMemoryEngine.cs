// DefaultMemoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultMemoryEngine : IMemoryEngine
    {
        public MemoryIndex State { get; private set; }

        public MemoryConfig Config { get; }

        private double _accHours;
        private readonly ILogger _log;

        public DefaultMemoryEngine(IOptions<MemoryConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger("Characters.Memory");
            State = new MemoryIndex(new List<EpisodicMemory>(), new Dictionary<string, SemanticFact>());
        }

        public void Encode(EpisodicMemory episode, IHumanContext ctx, IEventCollector outbox)
        {
            _log.LogDebug("[Memory] Zakódována epizoda: '{Tag}' (salience={Salience:F2}, emotion={Emotion}).", episode.What, episode.Salience, episode.Emotion);
            var list = State.Episodes.ToList();
            list.Add(episode);
            State = new MemoryIndex(list, State.Semantics);
            outbox.Add(new MemoryEncoded(episode.When, ctx.Id, episode.Id, episode.Strength));
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case Characters.Engines.Behavior.ActionCommitted ac:
                    Encode(new EpisodicMemory(Guid.NewGuid(), ac.OccurredAt, $"Action:{ac.ActionName}", SalienceForAction(ac.ActionName, ctx), EmotionFor(ac.ActionName, ctx.Snapshot.Psychology.Valence), Strength: 0.6), ctx, outbox);
                    break;

                case Characters.Engines.Interactions.InteractionOutcome io:
                    var tag = $"Interaction:{io.From}->{io.To}:{io.Reason}";
                    if (State.Episodes.Any(e => e.What == tag))
                    {
                        break;
                    }

                    var sal = 0.7 + (io.Accepted ? 0.2 : 0.0);
                    Encode(new EpisodicMemory(Guid.NewGuid(), io.OccurredAt, $"Interaction:{io.From}->{io.To}:{io.Reason}", sal, io.Accepted ? EmotionalTag.Positive : EmotionalTag.Negative, 0.7), ctx, outbox);
                    break;

                case Characters.Engines.Relationships.MicroPositive mp:
                    Encode(new EpisodicMemory(Guid.NewGuid(), mp.OccurredAt, $"Micro+:{mp.A}->{mp.B}:{mp.What}", 0.6, EmotionalTag.Positive, 0.6), ctx, outbox);
                    break;

                case Characters.Engines.Relationships.MicroNegative mn:
                    Encode(new EpisodicMemory(Guid.NewGuid(), mn.OccurredAt, $"Micro-:{mn.A}->{mn.B}:{mn.What}", 0.6, EmotionalTag.Negative, 0.6), ctx, outbox);
                    break;
            }
        }

        public IReadOnlyList<EpisodicMemory> Recall(Func<EpisodicMemory, bool> predicate)
        {
            return State.Episodes.Where(predicate).ToList();
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var h = Math.Max(0, dt.TotalHours);
            _accHours += h;

            // Zapomínání – lineární jednoduché (pro start)
            var episodes = State.Episodes.ToList();
            for (int i = 0; i < episodes.Count; i++)
            {
                var e = episodes[i];
                var newStrength = Math.Max(0, e.Strength - Config.ForgettingRate * (h / 24.0));
                episodes[i] = e with { Strength = newStrength };
            }

            // Konsolidace cca jednou denně
            if (_accHours >= 24.0)
            {
                _accHours -= 24.0;
                // Posil 10 nejvyšších saliencí
                var boosted = episodes
                    .OrderByDescending(e => e.Salience)
                    .Take(10)
                    .Select(e => e with { Strength = Math.Min(1.0, e.Strength + Config.SleepConsolidationBoost) })
                    .ToList();

                // Merge zpět
                var set = episodes.ToDictionary(e => e.Id);
                foreach (var b in boosted)
                {
                    set[b.Id] = b;
                }

                episodes = set.Values.ToList();

                outbox.Add(new MemoryConsolidated(now, ctx.Id, boosted.Count));
                _log.LogInformation("[Memory] Konsolidace: posíleno {Count} epizod.", boosted.Count);
            }

            episodes = episodes
                .Where(e => e.Strength >= Config.PruneThreshold)
                .ToList();

            State = new MemoryIndex(episodes, State.Semantics);
        }

        private static double SalienceForAction(string actionName, IHumanContext ctx)
        {
            return actionName switch
            {
                "Sleep" => 0.4,
                "Eat" => 0.5,
                "Drink" => 0.3,
                "ReachOut" => 0.6,
                "InviteIntimacy" => 0.8,
                _ => 0.5
            };
        }

        private static EmotionalTag EmotionFor(string actionName, double valence)
        {
            return actionName switch
            {
                "InviteIntimacy" or "ReachOut" or "SelfCare" => EmotionalTag.Positive,
                "Flee" or "Fight" => EmotionalTag.Negative,
                _ => valence switch
                {
                    > 0.05 => EmotionalTag.Positive,
                    < -0.05 => EmotionalTag.Negative,
                    _ => EmotionalTag.Neutral
                }
            };
        }

        public void RestoreState(MemoryIndex state) => State = state;
    }
}
