// DefaultPsychologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using System;
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultPsychologyEngine : IPsychologyEngine
    {
        public PsychologyState State { get; private set; }
        public PsychologyConfig Config { get; }

        private readonly ILogger _log;
        private readonly IRandomSource _rng;

        public DefaultPsychologyEngine(IOptions<PsychologyConfig> cfg, ILoggerFactory loggerFactory, IRandomSource rng)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger("Characters.Psychology");
            _rng = rng;

            State = new PsychologyState(
                Valence: 0.1, Arousal: 0.4, Dominance: 0.5,
                Stress: 20, CognitiveLoad: 20, DominantEmotion: DiscreteEmotion.Neutral);
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var h = Math.Max(0, dt.TotalHours);
            var s = State;
            var ph = ctx.Snapshot.Physiology;

            // Základní drift: stres klesá k baseline, PAD míří k neutrálu
            s = s with
            {
                Stress = Clamp01p(s.Stress - Config.StressRecoveryRatePerHour * h),
                Valence = Approach(s.Valence, 0, 0.05 * h),
                Arousal = Approach(s.Arousal, 0.5, 0.05 * h),
                Dominance = Approach(s.Dominance, 0.5, 0.03 * h)
            };

            // Fyzio modulace
            s = s with
            {
                Valence = Clampm1p1(s.Valence - 0.004 * ph.Hunger * h - 0.003 * ph.Pain * h + 0.0015 * ph.Energy * h),
                Stress = Clamp01p(s.Stress + 0.5 * Math.Min(8, ph.SleepDebtHours) * h + 0.05 * ph.Pain * h),
                Arousal = Clamp01(s.Arousal + 0.001 * ph.Thirst * h - 0.001 * ph.Energy * h)
            };

            // Ovulace – jemné zvýšení arousal/valence
            if (ph.Cycle?.OvulationWindow == true)
            {
                s = s with { Arousal = Clamp01(s.Arousal + 0.03), Valence = Clampm1p1(s.Valence + 0.02) };
            }

            // Náhodná denní variabilita
            var noise = (Config.BaselineAffectVariance <= 0) ? 0.0 : (RandomSym() * Config.BaselineAffectVariance);
            s = s with { Valence = Clampm1p1(s.Valence + noise) };

            var newDom = InferEmotion(s);
            if (newDom != s.DominantEmotion)
            {
                s = s with { DominantEmotion = newDom };
                outbox.Add(new EmotionShifted(now, ctx.Id, newDom, s.Valence, s.Arousal, s.Dominance));
            }

            State = s;

            double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
            double Clamp01p(double v) => Math.Max(0, Math.Min(100, v));
            double Clampm1p1(double v) => Math.Max(-1, Math.Min(1, v));
            static DiscreteEmotion InferEmotion(PsychologyState ps)
            {
                if (ps.Stress > 70) return ps.Valence < 0 ? DiscreteEmotion.Fear : DiscreteEmotion.Anger;
                if (ps.Valence > 0.4) return DiscreteEmotion.Joy;
                if (ps.Valence < -0.4) return DiscreteEmotion.Sadness;
                return DiscreteEmotion.Neutral;
            }
            double RandomSym() => (_rng.NextUnit() - 0.5) * 2.0 * 0.05; // ±5% šum
            double Approach(double val, double target, double by) =>
                (val < target) ? Math.Min(target, val + by) : Math.Max(target, val - by);
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            var s = State;

            switch (@event)
            {
                case Characters.Engines.Relationships.MicroPositive:
                    s = s with { Valence = Math.Min(1, s.Valence + 0.05) };
                    break;

                case Characters.Engines.Relationships.MicroNegative:
                    s = s with { Valence = Math.Max(-1, s.Valence - 0.06), Stress = Math.Min(100, s.Stress + 2) };
                    break;

                case Characters.Engines.Interactions.InteractionOutcome io:
                    if (io.Accepted) s = s with { Valence = Math.Min(1, s.Valence + 0.07) };
                    else s = s with { Valence = Math.Max(-1, s.Valence - 0.07), Stress = Math.Min(100, s.Stress + 3) };
                    break;

                case Characters.Engines.Psychology.StressSpiked sp:
                    s = s with { Stress = Math.Max(s.Stress, sp.NewStress) };
                    break;

                case Characters.Engines.Physiology.MensesStarted:
                    s = s with { Valence = Math.Max(-1, s.Valence - 0.05) };
                    break;

                case Characters.Engines.Physiology.OvulationWindowOpened:
                    s = s with { Arousal = Math.Min(1, s.Arousal + 0.05) };
                    break;

                case Characters.Engines.Memory.MemoryRecalled mr:
                    // Pokud epizoda existuje a je pozitivní, jemně přeladíme valenci
                    var ep = ctx.Snapshot.Memory.Episodes.Where(e => e.Id == mr.EpisodeId).FirstOrDefault();
                    if (ep is not null)
                    {
                        s = s with { Valence = Math.Clamp(s.Valence + (ep.Emotion == Characters.Engines.Memory.EmotionalTag.Positive ? +0.05 : -0.04), -1, 1) };
                    }
                    break;
            }

            State = s;
        }
    }
}

