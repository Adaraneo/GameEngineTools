// DefaultInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using System;
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultInteractionEngine : IInteractionEngine
    {
        public InteractionSurface State { get; private set; }
        public InteractionConfig Config { get; }

        public DefaultInteractionEngine(IOptions<InteractionConfig> cfg)
        {
            Config = cfg.Value;
            State = new InteractionSurface(Location: "Unknown", HasPrivacy: false, Noise: 0.5, Crowding: 0.5);
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Bez aktivního prostředí neděláme nic periodického.
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is InteractionProposed p)
            {
                // P(X = přijetí) ~ vztah + nálada + kontext - stres - šum
                var rels = ctx.Snapshot.Relationships.Edges;
                rels.TryGetValue(p.To, out var edge);

                var closeness = edge?.Closeness ?? 30;
                var comfort = edge?.Comfort ?? 30;
                var trust = edge?.Trust ?? 30;

                var psych = ctx.Snapshot.Psychology;
                var baseP = 0.30
                            + 0.0025 * closeness
                            + 0.0020 * comfort
                            + 0.0020 * trust
                            + 0.10 * Math.Max(0, psych.Valence)
                            + (State.HasPrivacy ? 0.05 : 0)
                            - 0.05 * State.Crowding
                            - 0.0015 * psych.Stress;

                var misattrib = Config.MisattributionRateBase * (psych.Stress / 100.0);
                baseP -= misattrib;

                var pAcc = Math.Clamp(baseP, 0.05, 0.95);
                var accepted = ctx.Random.Chance(pAcc);

                outbox.Add(new InteractionOutcome((p.OccurredAt), p.From, p.To, accepted, accepted ? "accepted" : "declined"));
            }
        }
    }
}

