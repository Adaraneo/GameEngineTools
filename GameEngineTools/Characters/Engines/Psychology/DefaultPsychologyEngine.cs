// DefaultPsychologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using System;
    using Characters.Core;
    using GameEngineTools.Logging;
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
            _log = loggerFactory.CreateLogger<DefaultPsychologyEngine>();
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
                Valence = Approach(s.Valence, 0, 0.15 * h),
                Arousal = Approach(s.Arousal, 0.5, 0.05 * h),
                Dominance = Approach(s.Dominance, 0.5, 0.03 * h)
            };

            // Fyzio modulace
            s = s with
            {
                Valence = Clampm1p1(s.Valence - 0.001 * ph.Hunger * h - 0.003 * ph.Pain * h + 0.0015 * ph.Energy * h),
                Stress = Clamp01p(s.Stress + 0.15 * Math.Min(8, ph.SleepDebtHours) * h + 0.05 * ph.Pain * h),
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
                // High stress — Dominance rozlišuje strach vs. hněv (PAD model)
                if (ps.Stress > 70)
                    return ps.Dominance < 0.4 ? DiscreteEmotion.Fear : DiscreteEmotion.Anger;
            
                // Surprise — náhlý arousal spike bez jasné valence
                if (ps.Arousal > 0.85 && ps.Valence is > -0.2 and < 0.2)
                    return DiscreteEmotion.Surprise;
            
                // Pride — pozitivní + vysoká dominance
                if (ps.Valence > 0.5 && ps.Dominance > 0.7)
                    return DiscreteEmotion.Pride;
            
                // Shame — negativní + nízká dominance + nízký stres (ne panic)
                if (ps.Valence < -0.3 && ps.Dominance < 0.3 && ps.Stress < 50)
                    return DiscreteEmotion.Shame;
            
                // Tenderness — pozitivní + klidný + submisivní (péče)
                if (ps.Valence > 0.3 && ps.Arousal < 0.4 && ps.Dominance < 0.45)
                    return DiscreteEmotion.Tenderness;
            
                // Disgust — negativní + nízký arousal + střední dominance
                if (ps.Valence < -0.4 && ps.Arousal < 0.4)
                    return DiscreteEmotion.Disgust;
            
                if (ps.Valence > 0.4) return DiscreteEmotion.Joy;
                if (ps.Valence < -0.4) return DiscreteEmotion.Sadness;
                return DiscreteEmotion.Neutral;
            }
            double RandomSym() => (_rng.NextUnit() - 0.5) * 2.0;
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
                    var self = ctx.Id;
                    bool wasRejected = io.From == self && !io.Accepted;
                    bool didReject = io.To == self && !io.Accepted;

                    if (io.Accepted)
                    {
                        s = s with { Valence = Math.Min(1, s.Valence + 0.07) };
                    }
                    else if (wasRejected)
                    {
                        var n = ctx.Personality.BigFive.Neuroticism;
                        var attachmentModifier = ctx.Personality.Attachment switch
                        {
                            AttachmentStyle.Secure => 0.0,
                            AttachmentStyle.Anxious => 0.08,
                            AttachmentStyle.Avoidant => -0.02,
                            AttachmentStyle.Disorganized => 0.12,
                            _ => 0.0
                        };
                        var actSensitivity = io.Act switch
                        {
                            SpeechAct.SelfDisclosure => 1.6,
                            SpeechAct.Invite => 1.4,
                            SpeechAct.Validation => 1.2,
                            SpeechAct.Meta => 1.1,
                            _ => 1.0
                        };
                        var impact = (0.05 + 0.10 * n + attachmentModifier) * actSensitivity;
                        s = s with
                        {
                            Valence = Math.Max(-1, s.Valence - impact),
                            Stress = Math.Min(100, s.Stress + 3 + 5 * n)
                        };
                    }
                    else if (didReject)
                    {
                        var guilt = 0.02 * ctx.Personality.BigFive.Agreeableness;
                        s = s with { Valence = Math.Max(-1, s.Valence - guilt) };
                    }
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

                // --- Konec spánkové session ---
                // Kvalita spánku ovlivňuje valenci a stres — závisí na Config.SleepQualityAffectWeight
                // a na osobnostním rysu Neuroticism (citlivější osobnosti reagují silněji).
                case Sleep.SleepEnded se:
                    {
                        var weight = Config.SleepQualityAffectWeight;          // 0–1, z appsettings
                        var neuroticism = ctx.Personality.BigFive.Neuroticism;       // 0–1
                        var sensitivityMod = 1.0 + neuroticism * 0.5;                // neurotici reagují až 1.5×

                        // Kvalita 0–100 → normalizujeme na -1..+1 (50 = neutrální bod)
                        var qualityNorm = (se.Quality - 50.0) / 50.0;               // -1 = hrozný, +1 = perfektní

                        // Valence: dobrý spánek zlepší náladu, špatný zhorší
                        var valenceDelta = qualityNorm * weight * 0.15 * sensitivityMod;
                        s = s with { Valence = Math.Clamp(s.Valence + valenceDelta, -1, 1) };

                        // Stres: přerušený nebo nekvalitní spánek přidá stres
                        if (se.WasInterrupted || se.Quality < 40)
                        {
                            var stressDelta = (1.0 - se.Quality / 100.0) * 10.0 * sensitivityMod;
                            s = s with { Stress = Math.Clamp(s.Stress + stressDelta, 0, 100) };
                            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
                            {
                        _log.PsychSleepInterrupted(se.Quality, stressDelta);
                            }
                        }
                        else
                        {
                            // Dobrý spánek snižuje stres navíc k průběžnému driftu v Tick()
                            var stressRelief = (se.Quality / 100.0) * 5.0;
                            s = s with { Stress = Math.Clamp(s.Stress - stressRelief, 0, 100) };
                        }

                        // Publikuj StressSpiked pokud stres přesáhl threshold (ostatní enginy mohou reagovat)
                        if (s.Stress > 70 && State.Stress <= 70)
                            outbox.Add(new StressSpiked(se.OccurredAt, ctx.Id, s.Stress));

                        break;
                    }

                // --- Noční můra ---
                // Přímý stresový spike + negativní valence; intenzita závisí na Neuroticism.
                case Sleep.NightmareTriggered nm:
                    {
                        var neuroticism = ctx.Personality.BigFive.Neuroticism;
                        var stressSpike = 8.0 + neuroticism * 12.0;               // 8–20 bodů stresu
                        var valencePenalty = 0.08 + neuroticism * 0.10;             // 0.08–0.18

                        s = s with
                        {
                            Stress = Math.Clamp(s.Stress + stressSpike, 0, 100),
                            Valence = Math.Clamp(s.Valence - valencePenalty, -1, 1),
                            Arousal = Math.Clamp(s.Arousal + 0.2, 0, 1)             // probuzení = vysoký arousal
                        };

                        outbox.Add(new StressSpiked(nm.OccurredAt, ctx.Id, s.Stress));
                        using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
                        {
                            _log.PsychNightmareEffect(ctx.Id.Value.ToString(), stressSpike, valencePenalty);
                        }

                        break;
                    }
            }

            State = s;
        }

        public void RestoreState(PsychologyState state) => State = state;
    }
}
