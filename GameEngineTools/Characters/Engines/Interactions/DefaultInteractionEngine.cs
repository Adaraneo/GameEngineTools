// DefaultInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using System;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultInteractionEngine : IInteractionEngine
    {
        public InteractionSurface State { get; private set; }
        public InteractionConfig Config { get; }
        private readonly ILogger _log;

        public DefaultInteractionEngine(IOptions<InteractionConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultInteractionEngine>();
            State = new InteractionSurface(Location: "Unknown", HasPrivacy: false, Noise: 0.5, Crowding: 0.5);
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Bez aktivního prostředí neděláme nic periodického.
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ContextChanged cc:
                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultInteractionEngine))))
                    {
                        _log.InteractionContextChanged(ctx.Id.Value.ToString(), cc.Location, cc.Noise, cc.Crowding);
                    }

                    State = new InteractionSurface(
                        Location: cc.Location,
                        HasPrivacy: cc.HasPrivacy,
                        Noise: Math.Clamp(cc.Noise, 0, 1),
                        Crowding: Math.Clamp(cc.Crowding, 0, 1));
                    break;

                case InteractionProposed p:
                    if (p.To != ctx.Id)
                    {
                        break;
                    }

                    var rels = ctx.Snapshot.Relationships.Edges;
                    rels.TryGetValue(p.From, out var edge);

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

                    using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultInteractionEngine))))
                    {
                        _log.InteractionOutcomeDecided(ctx.Id.Value.ToString(), p.From.Value.ToString(), p.To.Value.ToString(), pAcc, accepted ? "PŘIJATO" : "ODMÍTNUTO");
                    }

                    outbox.Add(new InteractionOutcome((p.OccurredAt), p.From, p.To, accepted, accepted ? "accepted" : "declined"));
                    break;
            }
        }

        public void RestoreState(InteractionSurface state) => State = state;
    }
}
