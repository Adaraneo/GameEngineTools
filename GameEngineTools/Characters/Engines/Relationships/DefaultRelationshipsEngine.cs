// DefaultRelationshipsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultRelationshipsEngine : IRelationshipsEngine
    {
        public RelationshipState State { get; private set; }

        public RelationshipsConfig Config { get; }

        private readonly ILogger _log;

        public DefaultRelationshipsEngine(IOptions<RelationshipsConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger("Characters.Relationships");
            State = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>());
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            var self = ctx.Id;
            switch (@event)
            {
                case FirstImpressionFormed fi:
                    Upsert(self, fi.B, e => e with
                    {
                        Like = Lerp(e.Like, fi.Like, 0.7),
                        Attraction = Lerp(e.Attraction, fi.Attraction, 0.7),
                        Trust = e.Trust <= 0 ? 45 : e.Trust,
                        Closeness = Math.Max(e.Closeness, 10)
                    });
                    break;

                case MicroPositive mp:
                    Upsert(self, mp.B, e => e with
                    {
                        Like = Bump(e.Like, +2.0),
                        Trust = Bump(e.Trust, +1.0),
                        Closeness = Bump(e.Closeness, +1.5),
                        Comfort = Bump(e.Comfort, +2.0)
                    });
                    break;

                case MicroNegative mn:
                    Upsert(self, mn.B, e => e with
                    {
                        Like = Bump(e.Like, -2.5),
                        Trust = Bump(e.Trust, -2.0),
                        Comfort = Bump(e.Comfort, -2.0)
                    });
                    break;

                case RepairAttempt ra:
                    Upsert(self, ra.B, e => e with
                    {
                        Trust = Bump(e.Trust, ra.Accepted ? +4.0 : -4.0),
                        Closeness = Bump(e.Closeness, ra.Accepted ? +3.0 : -3.0)
                    });
                    break;

                case Interactions.InteractionOutcome io when io.Accepted:
                    var otherId = io.From == self ? io.To : io.From;
                    if (!State.Edges.ContainsKey(otherId))
                    {
                        Upsert(self, otherId, e => e);
                    }

                    Upsert(self, otherId, e => e with
                    {
                        Closeness = Math.Min(100, e.Closeness + 1.5),
                        Like = Math.Min(100, e.Like + 0.5),
                        Comfort = Math.Min(100, e.Comfort + 0.8)
                    });
                    break;

                case Interactions.InteractionOutcome io when !io.Accepted:
                    var otherId2 = io.From == self ? io.To : io.From;
                    if (!State.Edges.ContainsKey(otherId2))
                    {
                        Upsert(self, otherId2, e => e);
                    }

                    break;
            }
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0, dt.TotalDays);
            if (days == 0)
            {
                return;
            }

            if (State.Edges.Count == 0)
            {
                return;
            }

            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);
            foreach (var kv in State.Edges)
            {
                var e = kv.Value;
                var d = Config.DecayPerDay * days;
                var like = Approach(e.Like, 50, d);
                var trust = Approach(e.Trust, 50, d * 0.5);
                var attraction = Approach(e.Attraction, e.Attraction > 50 ? 45 : 35, d * 0.4);
                var close = Approach(e.Closeness, 35, d * 1.2);
                var respect = Approach(e.Respect, 55, d * 0.3);
                var comfort = Approach(e.Comfort, 45, d * 0.6);

                var psych = ctx.Snapshot.Psychology;
                var valenceEffect = psych.Valence * 0.3 * days;
                var stressEffect = psych.Stress * 0.02 * days;

                dict[kv.Key] = e with
                {
                    Like = Clamp(like + valenceEffect - stressEffect),
                    Trust = Clamp(trust),
                    Attraction = Clamp(attraction),
                    Closeness = Clamp(close),
                    Respect = Clamp(respect),
                    Comfort = Clamp(comfort + valenceEffect * 0.5 - stressEffect * 0.5)
                };
            }

            State = new RelationshipState(dict);

            static double Clamp(double v) => Math.Max(0, Math.Min(100, v));
            static double Approach(double cur, double target, double amount) =>
                (cur < target) ? Math.Min(target, cur + amount) : Math.Max(target, cur - amount);
        }

        private void Upsert(HumanId self, HumanId other, Func<RelationshipEdge, RelationshipEdge> mut)
        {
            var dict = new Dictionary<HumanId, RelationshipEdge>(State.Edges);
            if (!dict.TryGetValue(other, out var e))
            {
                e = new RelationshipEdge(
                    A: self, B: other,
                    Like: 45, Trust: 45, Attraction: 35, Closeness: 10, Respect: 55, Comfort: 40,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));
                _log.LogInformation("[Relationships] Nová hrana: {A} → {B}.", self.Value, other.Value);
            }
            var updated = mut(e);
            _log.LogDebug("[Relationships] Hrana {A}→{B}: Like={Like:F1}, Trust={Trust:F1}, Closeness={Closeness:F1}.", self.Value, other.Value, updated.Like, updated.Trust, updated.Closeness);
            dict[other] = updated;
            State = new RelationshipState(dict);
        }

        private static double Bump(double v, double by) => Math.Max(0, Math.Min(100, v + by));

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

        public void RestoreState(RelationshipState state) => State = state;
    }
}
