// DefaultPsychologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using System;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default implementation of the psychology engine.
    /// Models affective state using the PAD (Pleasure-Arousal-Dominance) model, infers a
    /// discrete dominant emotion each tick, and reacts to a wide range of domain events
    /// (sleep, pregnancy, social interactions, memory recall, nightmares).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine maintains three continuous PAD dimensions plus a <c>Stress</c> scalar and a
    /// <c>CognitiveLoad</c> scalar. Each <see cref="Tick"/> applies:
    /// <list type="number">
    ///   <item><description>Stress decay toward zero at <c>StressRecoveryRatePerHour</c>.</description></item>
    ///   <item><description>PAD drift toward a neutral resting state.</description></item>
    ///   <item><description>Physiology modulation (pain → stress, sleep debt → CogLoad, fever → arousal suppression).</description></item>
    ///   <item><description>Ovulation arousal/valence boost when the cycle window is open.</description></item>
    ///   <item><description>Random daily affective noise scaled by <c>BaselineAffectVariance</c>.</description></item>
    ///   <item><description>Discrete emotion inference via the PAD rule table.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="Handle"/> applies instantaneous state changes for named domain events and
    /// may emit <see cref="StressSpiked"/> when stress crosses the 70-point threshold from below.
    /// </para>
    /// </remarks>
    internal sealed class DefaultPsychologyEngine : IPsychologyEngine
    {
        /// <summary>Gets the current psychological state of the character.</summary>
        public PsychologyState State { get; private set; }

        /// <summary>Gets the configuration driving affect drift rates, CognitiveLoad weights, and fever thresholds.</summary>
        public PsychologyConfig Config { get; }

        private readonly ILogger _log;
        private readonly IRandomSource _rng;
        private WDateTime? _stressAbove70Since;

        /// <summary>
        /// Initialises the engine with a neutral resting state:
        /// Valence=0.1, Arousal=0.4, Dominance=0.5, Stress=20, CognitiveLoad=20.
        /// </summary>
        /// <param name="cfg">Psychology configuration options.</param>
        /// <param name="loggerFactory">Logger factory injected by the DI container.</param>
        /// <param name="rng">Random source for daily affect noise; use <c>ZeroRandom</c> in tests.</param>
        public DefaultPsychologyEngine(IOptions<PsychologyConfig> cfg, ILoggerFactory loggerFactory, IRandomSource rng)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultPsychologyEngine>();
            _rng = rng;

            State = new PsychologyState(
                Valence: 0.1, Arousal: 0.4, Dominance: 0.5,
                Stress: 20, CognitiveLoad: 20, DominantEmotion: DiscreteEmotion.Neutral,
                MoodBaseline: 50, Motivations: new MotivationState());
        }

        /// <summary>
        /// Advances continuous psychological state drift by one time step.
        /// Called each game tick to apply gradual changes to PAD dimensions, stress, and
        /// cognitive load; infers a new dominant emotion and emits <see cref="EmotionShifted"/>
        /// if the inferred label changes.
        /// </summary>
        /// <param name="now">Current in-world date-time (used for event timestamps).</param>
        /// <param name="dt">
        /// Elapsed time since the last tick. All drift deltas are scaled by
        /// <c>dt.TotalHours</c> so the engine is frame-rate independent.
        /// </param>
        /// <param name="ctx">
        /// Character context providing physiology snapshot (for stress/CogLoad modulation)
        /// and the current action name (Sleep/Idle accelerate CogLoad recovery).
        /// </param>
        /// <param name="outbox">Collector for <see cref="EmotionShifted"/> events.</param>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var h = Math.Max(0, dt.TotalHours);
            var s = State;
            var ph = ctx.Snapshot.Physiology;
            var action = ctx.Snapshot.Behavior.CurrentPlan?.Name;

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
                Arousal = Clamp01(s.Arousal + 0.001 * ph.Thirst * h - 0.001 * ph.Energy * h),
                Dominance = Clamp01(s.Dominance - 0.0005 * ph.Pain * h - 0.01 * Math.Max(0, ph.BodyTempDelta - 1.5) * h)
            };

            // Nutriční dopady — nízké železo snižuje valenci; nízký vitamín D tlumí náladu
            if (ph.Nutrition is { } nutrition)
            {
                if (nutrition.Iron < 30)
                    s = s with { Valence = Clampm1p1(s.Valence - (30 - nutrition.Iron) * Config.LowIronValencePenaltyPerUnit * h) };
                if (nutrition.VitaminD < 20)
                    s = s with { MoodBaseline = Math.Clamp(s.MoodBaseline - (20 - nutrition.VitaminD) * Config.LowVitaminDMoodPenaltyPerHour / 20.0 * h, 0, 100) };
            }

            // Sickness behavior — imunitní zátěž → sociální stažení + anhedonie + letargie (Dantzer 2007)
            if (s.Motivations is { } sicknessMotiv)
            {
                var shouldWithdraw = ph.ImmuneLoad > 50;
                if (shouldWithdraw != sicknessMotiv.SicknessWithdraw)
                {
                    var next = sicknessMotiv with { SicknessWithdraw = shouldWithdraw };
                    s = s with { Motivations = next };
                    outbox.Add(new MotivationChanged(now, ctx.Id, sicknessMotiv, next));
                }

                // Anhedonie (Dantzer): IL-1β inhibuje nucleus accumbens →
                // letargie (Arousal↓) a brain fog (CogLoad↑) při aktivní nemoci
                if (shouldWithdraw)
                {
                    s = s with
                    {
                        Arousal       = Clamp01(s.Arousal - Config.SicknessLethargyArousalPenalty * h),
                        CognitiveLoad = Clamp01p(s.CognitiveLoad + Config.SicknessBrainFogCogLoadBonus * h)
                    };
                }
            }

            // CognitiveLoad — odvozuje se z fyziologických stressorů
            {
                var feverThreshold = 1.5;
                var feverDegrees = Math.Max(0, ph.BodyTempDelta - feverThreshold);
                var targetLoad = Clamp01p(
                    ph.SleepDebtHours * Config.CognitiveLoadSleepDebtWeight
                    + ph.Pain          * Config.CognitiveLoadPainWeight
                    + s.Stress         * Config.CognitiveLoadStressWeight
                    + feverDegrees     * Config.FeverCognitiveLoadPerDegree);

                var recoveryRate = (action is GameEngineTools.Characters.Engines.ActionNames.Sleep
                                       or GameEngineTools.Characters.Engines.ActionNames.Idle)
                    ? Config.CognitiveLoadRecoveryPerHour * 1.5 * h
                    : Config.CognitiveLoadRecoveryPerHour * h;
                var buildRate = 8.0 * h;
                var cogByRate = s.CognitiveLoad < targetLoad ? buildRate : recoveryRate;
                s = s with { CognitiveLoad = Clamp01p(Approach(s.CognitiveLoad, targetLoad, cogByRate)) };

                if (feverDegrees > 0)
                    s = s with { Arousal = Clamp01(s.Arousal - feverDegrees * Config.FeverArousalSuppressPerDegree * h) };
                // Vysoká horečka (> 2.5°C) posouvá náladu k negativní valenci (zmatenost)
                if (ph.BodyTempDelta > 2.5)
                    s = s with { Valence = Clampm1p1(s.Valence - 0.02 * h) };
            }

            // Allostatická zátěž — zvyšuje CogLoad a snižuje valenci
            var alloLoad = ph.AllostaticLoad;
            if (alloLoad > 0)
            {
                s = s with
                {
                    CognitiveLoad = Clamp01p(s.CognitiveLoad + alloLoad * Config.AllostaticLoadCognitiveWeight * h),
                    Valence       = Clampm1p1(s.Valence - alloLoad * 0.001 * h)
                };
            }

            // Kortizol → stres a arousal (HPA over-activation)
            if (ph.CortisolLevel > 70)
                s = s with { Stress = Clamp01p(s.Stress + (ph.CortisolLevel - 70) * Config.CortisolStressWeight * h) };
            s = s with { Arousal = Clamp01(s.Arousal + (ph.CortisolLevel - 50) * Config.CortisolArousalWeight * h) };

            // Sleep Inertia — kognitivní zpomalení a tlumení arousalu po probuzení (Borbély)
            if (ph.SleepInertiaHours > 0)
            {
                var inertiaSeverity = ph.SleepInertiaHours / Config.SleepInertiaMaxHours; // 0..1
                s = s with
                {
                    Arousal       = Clamp01(s.Arousal - inertiaSeverity * 0.15 * h),
                    CognitiveLoad = Clamp01p(s.CognitiveLoad + inertiaSeverity * 5.0 * h)
                };
            }

            // Hangry neutrální bias — hlad misattribuovaný k negativitě v neutrálním kontextu
            // (MacCormack & Lindquist, 2019: interoceptivní signál hladu → hostile attribution bias)
            if (ph.Hunger > Config.HangryNeutralBiasThreshold
                && Math.Abs(s.Valence) < Config.HangryNeutralContextWindow)
            {
                var hungerExcess = (ph.Hunger - Config.HangryNeutralBiasThreshold) / 30.0; // 0..1
                s = s with { Valence = Clampm1p1(s.Valence - hungerExcess * Config.HangryNeutralBiasStrength * h) };
            }

            // Testosteron → NeedIntimacy a stresová resilience (jen muži)
            if (ph.Testosterone is { } testo && s.Motivations is { } motiv)
            {
                if (testo.Level > 65)
                {
                    var intimacyBoost = (testo.Level - 65) * Config.TestosteroneIntimacyWeight * h;
                    var next = motiv with { NeedIntimacy = Math.Min(100, motiv.NeedIntimacy + intimacyBoost) };
                    if (next != motiv)
                    {
                        s = s with { Motivations = next };
                        outbox.Add(new MotivationChanged(now, ctx.Id, motiv, next));
                    }
                }
                // Stress resilience: vyšší testosteron zpomaluje akumulaci stresu
                if (testo.Level > 50)
                    s = s with { Stress = Math.Max(0, s.Stress - (testo.Level - 50) * Config.TestosteroneStressResilienceWeight * h) };
            }

            // MoodBaseline — pomalý drift směrem k neutrálu (50), potlačený vysokým stresem
            {
                var moodRecovery = Config.MoodBaselineRecoveryPerHour;
                if (s.Stress > Config.MoodBaselineHighStressThreshold) moodRecovery *= 0.1;
                moodRecovery *= 1.0 + ctx.Personality.BigFive.Agreeableness * Config.MoodBaselineAgreeablenessBonus;
                var alloMoodDampFactor = 1.0 - alloLoad / 200.0;
                s = s with { MoodBaseline = Math.Clamp(Approach(s.MoodBaseline, 50, moodRecovery * alloMoodDampFactor * h), 0, 100) };
            }

            // Cirkadiánní rytmus — dvě Gaussovy křivky (ráno + večer) s poobědovým poklesem.
            // Vrcholy jsou posunuty o CircadianPhaseShiftHours (chronotyp + jet-lag) z Physiology.
            if (Config.EnableCircadianRhythm)
            {
                var hoursOfDay  = (double)(now.Hour % WWorld.Spec.HoursPerDay);
                var phaseShift  = ph.CircadianPhaseShiftHours;
                var morningPeak = 0.35 * Math.Exp(-Math.Pow(hoursOfDay - 10.0 - phaseShift, 2) / 16.0);  // σ²=8, peak 10h ± posun
                var eveningPeak = 0.25 * Math.Exp(-Math.Pow(hoursOfDay - 19.0 - phaseShift, 2) / 12.0);  // σ²=6, peak 19h ± posun
                var lunchDip    = 0.20 * Math.Exp(-Math.Pow(hoursOfDay - 15.0 - phaseShift, 2) / 3.0);   // σ²=1.5, dip 15h ± posun
                var baseArousal = Math.Clamp(0.60 + morningPeak + eveningPeak - lunchDip, 0.40, 0.95);
                var delta = (baseArousal - s.Arousal) * Config.CircadianInfluence * h;
                s = s with { Arousal = Clamp01(s.Arousal + delta) };
            }

            // Stresová manifestace — sleduj jak dlouho je stres povýšen nad threshold
            if (s.Stress > Config.StressManifestationThreshold)
            {
                _stressAbove70Since ??= now;
                var hoursElevated = (now - _stressAbove70Since.Value).TotalHours;
                if (hoursElevated >= Config.StressManifestationHours)
                {
                    outbox.Add(new StressManifested(now, ctx.Id, ChooseManifestation(ctx.Personality)));
                    _stressAbove70Since = now; // reset — nezahlcovat výstupní frontu
                }
            }
            else
            {
                _stressAbove70Since = null;
            }

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

        /// <summary>
        /// Reacts to discrete domain events by applying instantaneous psychological state mutations.
        /// Handled event types and their effects:
        /// <list type="bullet">
        ///   <item><description><b>MicroPositive / MicroNegative</b> — minor relationship micro-events; nudge Valence.</description></item>
        ///   <item><description><b>InteractionOutcome</b> — accepted/rejected social interactions; adjust Valence, Stress, Dominance scaled by personality (Neuroticism, Attachment style, act sensitivity).</description></item>
        ///   <item><description><b>StressSpiked</b> — external stress signal; raises Stress to at least <c>sp.NewStress</c>.</description></item>
        ///   <item><description><b>MensesStarted</b> — reduces Valence by 0.05 (physical discomfort onset).</description></item>
        ///   <item><description><b>OvulationWindowOpened</b> — raises Arousal by 0.05 (hormonal activation).</description></item>
        ///   <item><description><b>PregnancyStarted</b> — subtle hormonal lability: small Arousal and Valence increase.</description></item>
        ///   <item><description><b>PregnancyDiscovered</b> — stress spike proportional to Neuroticism; may emit <see cref="StressSpiked"/>.</description></item>
        ///   <item><description><b>ChildBorn</b> — Valence +0.25, Arousal +0.15, Dominance −0.10, Stress −10.</description></item>
        ///   <item><description><b>MemoryRecalled</b> — nudges Valence ±0.04–0.05 based on episode emotional tag.</description></item>
        ///   <item><description><b>SleepEnded</b> — sleep quality shifts Valence; poor/interrupted sleep adds stress; good sleep reduces stress; may emit <see cref="StressSpiked"/>.</description></item>
        ///   <item><description><b>NightmareTriggered</b> — stress spike 8–20 pts, Valence penalty 0.08–0.18, Arousal +0.2; always emits <see cref="StressSpiked"/>.</description></item>
        /// </list>
        /// </summary>
        /// <param name="event">The domain event to handle.</param>
        /// <param name="ctx">Character context used for personality traits (Neuroticism, Agreeableness, Attachment) and episode memory lookups.</param>
        /// <param name="outbox">Collector for follow-on events (<see cref="StressSpiked"/>).</param>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            var s = State;
            var ph = ctx.Snapshot.Physiology;

            switch (@event)
            {
                case Characters.Engines.Relationships.MicroPositive:
                    s = s with { Valence = Math.Min(1, s.Valence + 0.05) };
                    break;

                case Characters.Engines.Relationships.MicroNegative:
                    s = s with { Valence = Math.Max(-1, s.Valence - 0.06), Stress = Math.Min(100, s.Stress + 2) };
                    if (ph.Hunger > 40)
                    {
                        var hungerNorm = (ph.Hunger - 40.0) / 60.0;
                        s = s with
                        {
                            Arousal = Math.Clamp(s.Arousal + 0.05 * hungerNorm, 0, 1),
                            Valence = Math.Clamp(s.Valence  - 0.08 * hungerNorm, -1, 1)
                        };
                    }
                    break;

                case Characters.Engines.Interactions.InteractionOutcome io:
                    var self = ctx.Id;
                    bool wasRejected = io.From == self && !io.Accepted;
                    bool didReject = io.To == self && !io.Accepted;

                    if (io.Accepted)
                    {
                        // Anhedonie při sickness: pozitivní interakce méně uspokojuje (Dantzer 2007)
                        var anhedoniaMult = (ph.ImmuneLoad > Config.SicknessAnhedoniaImmuneThreshold)
                            ? Config.SicknessAnhedoniaRewardBlunting : 1.0;
                        var prevMotivAcc = s.Motivations ?? new MotivationState();
                        s = s with
                        {
                            Valence = Math.Min(1, s.Valence + 0.07),
                            Dominance = Math.Clamp(s.Dominance + 0.03, 0, 1),
                            MoodBaseline = Math.Clamp(s.MoodBaseline + 5.0, 0, 100),
                            Motivations = prevMotivAcc with
                            {
                                NeedSocial = Math.Clamp(prevMotivAcc.NeedSocial + 3.0 * anhedoniaMult, 0, 100)
                            }
                        };
                        if (s.Motivations != prevMotivAcc)
                            outbox.Add(new MotivationChanged(io.OccurredAt, ctx.Id, prevMotivAcc, s.Motivations!));
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
                        var prevMotivRej = s.Motivations ?? new MotivationState();
                        s = s with
                        {
                            Valence = Math.Max(-1, s.Valence - impact),
                            Stress = Math.Min(100, s.Stress + 3 + 5 * n),
                            Dominance = Math.Clamp(s.Dominance - 0.04 * actSensitivity, 0, 1),
                            MoodBaseline = Math.Clamp(s.MoodBaseline - 8.0, 0, 100),
                            Motivations = prevMotivRej with
                            {
                                NeedSocial = Math.Clamp(prevMotivRej.NeedSocial - 5.0, 0, 100),
                                NeedSafety = Math.Clamp(prevMotivRej.NeedSafety + 5.0, 0, 100)
                            }
                        };
                        if (s.Motivations != prevMotivRej)
                            outbox.Add(new MotivationChanged(io.OccurredAt, ctx.Id, prevMotivRej, s.Motivations!));
                        if (ph.Hunger > 40)
                        {
                            var hungerNorm = (ph.Hunger - 40.0) / 60.0;
                            s = s with
                            {
                                Arousal = Math.Clamp(s.Arousal + 0.05 * hungerNorm, 0, 1),
                                Valence = Math.Clamp(s.Valence  - 0.08 * hungerNorm, -1, 1)
                            };
                        }
                    }
                    else if (didReject)
                    {
                        var guilt = 0.02 * ctx.Personality.BigFive.Agreeableness;
                        s = s with
                        {
                            Valence = Math.Max(-1, s.Valence - guilt),
                            Dominance = Math.Clamp(s.Dominance + 0.02, 0, 1)
                        };
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

                case Characters.Engines.Physiology.PregnancyStarted:
                    // Hormonální nástup (skrytý) — jemná labilita
                    s = s with
                    {
                        Arousal = Math.Clamp(s.Arousal + 0.04, 0, 1),
                        Valence = Math.Clamp(s.Valence + 0.02, -1, 1)
                    };
                    break;

                case Characters.Engines.Physiology.PregnancyDiscovered pd:
                    {
                        var n = ctx.Personality.BigFive.Neuroticism;
                        var openness = ctx.Personality.BigFive.Openness;
                        var stressSpike = 10.0 + n * 15.0;
                        var valenceDelta = -0.05 + openness * 0.08 - n * 0.06;
                        s = s with
                        {
                            Stress = Math.Clamp(s.Stress + stressSpike, 0, 100),
                            Arousal = Math.Clamp(s.Arousal + 0.10 + n * 0.08, 0, 1),
                            Valence = Math.Clamp(s.Valence + valenceDelta, -1, 1)
                        };
                        if (s.Stress > 70 && State.Stress <= 70)
                            outbox.Add(new StressSpiked(pd.OccurredAt, ctx.Id, s.Stress));
                        var prevMotivPd = State.Motivations ?? new MotivationState();
                        s = s with
                        {
                            Motivations = prevMotivPd with
                            {
                                NeedCare     = Math.Clamp(prevMotivPd.NeedCare     + 15.0, 0, 100),
                                NeedIntimacy = Math.Clamp(prevMotivPd.NeedIntimacy -  5.0, 0, 100)
                            }
                        };
                        if (s.Motivations != prevMotivPd)
                            outbox.Add(new MotivationChanged(pd.OccurredAt, ctx.Id, prevMotivPd, s.Motivations!));
                        break;
                    }

                case Characters.Engines.Physiology.ChildBorn cb:
                    var prevMotivCb = s.Motivations ?? new MotivationState();
                    s = s with
                    {
                        Valence   = Math.Clamp(s.Valence + 0.25, -1, 1),
                        Arousal   = Math.Clamp(s.Arousal + 0.15, 0, 1),
                        Dominance = Math.Clamp(s.Dominance - 0.10, 0, 1),
                        Stress    = Math.Clamp(s.Stress - 10, 0, 100),
                        Motivations = prevMotivCb with
                        {
                            NeedCare     = Math.Clamp(prevMotivCb.NeedCare     + 20.0, 0, 100),
                            NeedIntimacy = Math.Clamp(prevMotivCb.NeedIntimacy - 10.0, 0, 100)
                        }
                    };
                    if (s.Motivations != prevMotivCb)
                        outbox.Add(new MotivationChanged(cb.OccurredAt, ctx.Id, prevMotivCb, s.Motivations!));
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
                            s = s with
                            {
                                Stress = Math.Clamp(s.Stress + stressDelta, 0, 100),
                                MoodBaseline = Math.Clamp(s.MoodBaseline - 2.0, 0, 100)
                            };
                            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
                            {
                        _log.PsychSleepInterrupted(se.Quality, stressDelta);
                            }
                        }
                        else
                        {
                            // Dobrý spánek snižuje stres navíc k průběžnému driftu v Tick()
                            var stressRelief = (se.Quality / 100.0) * 5.0;
                            var moodGain = se.Quality > 70 ? 2.0 : 0.0;
                            s = s with
                            {
                                Stress = Math.Clamp(s.Stress - stressRelief, 0, 100),
                                MoodBaseline = Math.Clamp(s.MoodBaseline + moodGain, 0, 100)
                            };
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

        private static string ChooseManifestation(Traits.Personality p)
        {
            if (p.BigFive.Neuroticism > 0.6)
                return p.BigFive.Extraversion < 0.4 ? "withdrawal" : "anxiety";
            if (p.BigFive.Agreeableness < 0.35)
                return "aggression";
            if (p.BigFive.Openness > 0.6)
                return p.BigFive.Conscientiousness > 0.5 ? "creativity" : "rumination";
            return "anxiety";
        }

        /// <summary>
        /// Replaces the current state with the provided snapshot.
        /// Used by the persistence layer after a save/load cycle and by tests to establish
        /// specific initial psychological conditions.
        /// </summary>
        /// <param name="state">The psychology state to restore.</param>
        public void RestoreState(PsychologyState state) => State = state;
    }
}
