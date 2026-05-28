// DefaultPsychologyEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using System;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using GameEngineTools.World.Objects;

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
        private double _previousAllostaticLoad;

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

            // Vagální tonus (přes Neuroticism): High N = nižší HRV = pomalejší stress recovery
            // Empiricky: Neuroticism negativně koreluje s vagálním tónem
            var vagalTone = 1.0 - ctx.Personality.BigFive.Neuroticism * 0.5;  // 0.5..1.0

            // Neuroticism moduluje rychlost AKUMULACE stresu (HPA osa vulnerabilita).
            // Empiricky: max ~1.7× rozdíl mezi nízkým a vysokým N (Sutin et al. 2013;
            // Bibbey et al. 2015 — HPA reactivity); nikoli 3×.
            var stressGrowthMult = 0.65 + ctx.Personality.BigFive.Neuroticism * 1.05;  // [0.65, 1.70]

            // Stresová vulnerabilita v noci (McEwen 1998): kortizol moduluje HPA resilience
            // Nízký kortizol (noc) → stres klesá pomaleji; Vysoký (ráno) → rychleji
            var circadianVulnerability = Math.Clamp(
                ph.CortisolLevel / Config.CircadianVulnerabilityScale,
                Config.CircadianVulnerabilityMin, 2.0);

            // Per-emotion valence decay multiplier (Verduyn & Lavrijsen 2015):
            // different emotions have empirically different durations — sadness lingers, fear fades fast.
            var emotionDecayMult = GetEmotionDecayMultiplier(s.DominantEmotion, Config);

            // Rumination: high stress blocks decay of ruminative negative emotions (Nolen-Hoeksema 2000)
            if (s.Stress > Config.RuminationStressThreshold &&
                s.DominantEmotion is DiscreteEmotion.Sadness or DiscreteEmotion.Shame or DiscreteEmotion.Anger)
            {
                var ruminationBlock = 1.0 - s.Stress / 100.0 * Config.RuminationDecayBlock;
                emotionDecayMult *= Math.Max(0.01, ruminationBlock);
            }

            // Základní drift: stres klesá k baseline (modulován vagálním tónem + cirkadiánní vulnerabilitou)
            s = s with
            {
                Stress = Clamp01p(s.Stress - Config.StressRecoveryRatePerHour * vagalTone * circadianVulnerability * h),
                Valence = Approach(s.Valence, 0, 0.15 * emotionDecayMult * h),
                Arousal = Approach(s.Arousal, 0.5, 0.05 * h),
                Dominance = Approach(s.Dominance, 0.5, 0.03 * h)
            };

            // Hyperalgezie: imunitní zátěž zesiluje bolestivý signál (Dantzer 2007)
            var painAmp = ph.ImmuneLoad > Config.HyperalgesiaImmuneThreshold
                ? 1.0 + (ph.ImmuneLoad - Config.HyperalgesiaImmuneThreshold) / 60.0 * Config.HyperalgesiaMaxMultiplier
                : 1.0;

            // Fyzio modulace (Pain efekty škálovány hyperalgezií)
            s = s with
            {
                Valence = Clampm1p1(s.Valence - 0.001 * ph.Hunger * h - 0.003 * ph.Pain * painAmp * h + 0.0015 * ph.Energy * h),
                // stressGrowthMult: High Neuroticism → HPA osa reaguje silněji na stejné stresory
                Stress = Clamp01p(s.Stress + (0.15 * Math.Min(8, ph.SleepDebtHours) * h + 0.05 * ph.Pain * painAmp * h) * stressGrowthMult),
                Arousal = Clamp01(s.Arousal + 0.001 * ph.Thirst * h - 0.001 * ph.Energy * h),
                Dominance = Clamp01(s.Dominance - 0.0005 * ph.Pain * painAmp * h - 0.01 * Math.Max(0, ph.BodyTempDelta - 1.5) * h)
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
                        Arousal = Clamp01(s.Arousal - Config.SicknessLethargyArousalPenalty * h),
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
                    + ph.Pain * Config.CognitiveLoadPainWeight
                    + s.Stress * Config.CognitiveLoadStressWeight
                    + feverDegrees * Config.FeverCognitiveLoadPerDegree);

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
                    Valence = Clampm1p1(s.Valence - alloLoad * 0.001 * h)
                };
            }

            // Kortizol → stres a arousal (HPA over-activation); modulován Neuroticism growth mult
            if (ph.CortisolLevel > 70)
                s = s with { Stress = Clamp01p(s.Stress + (ph.CortisolLevel - 70) * Config.CortisolStressWeight * h * stressGrowthMult) };
            s = s with { Arousal = Clamp01(s.Arousal + (ph.CortisolLevel - 50) * Config.CortisolArousalWeight * h) };

            // Sleep Inertia — kognitivní zpomalení a tlumení arousalu po probuzení (Borbély)
            if (ph.SleepInertiaHours > 0)
            {
                var inertiaSeverity = ph.SleepInertiaHours / Config.SleepInertiaMaxHours; // 0..1
                s = s with
                {
                    Arousal = Clamp01(s.Arousal - inertiaSeverity * 0.15 * h),
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

            // Wanting vs. Liking: stres amplifikuje wanting/craving (Berridge 2025)
            // Liking suppression již pokryta anhedonií; chybějící část: dopaminergní wanting pod stresem
            if (s.Stress > Config.WantingStressThreshold && s.Motivations is { } wantingMotiv)
            {
                var stressExcess = (s.Stress - Config.WantingStressThreshold) / 40.0;
                var nextWanting = wantingMotiv with
                {
                    NeedIntimacy = Math.Min(100, wantingMotiv.NeedIntimacy + stressExcess * Config.WantingNeedIntimacyBoostPerHour * h),
                    NeedSocial = Math.Min(100, wantingMotiv.NeedSocial + stressExcess * Config.WantingNeedSocialBoostPerHour * h)
                };
                if (nextWanting != wantingMotiv)
                {
                    s = s with { Motivations = nextWanting };
                    outbox.Add(new MotivationChanged(now, ctx.Id, wantingMotiv, nextWanting));
                }
            }

            // ── Dual Control Model (Bancroft & Janssen 2000) ─────────────────────────
            // SES/SIS1/SIS2 modulate NeedIntimacy independently of testosterone and stress-wanting.
            // null DualControl = population average (SES=0.5, SIS1=0.5, SIS2=0.5) → no net change.
            if (s.Motivations is { } dcmMotiv && ctx.Personality.DualControl is { } dcm)
            {
                var crowding = double.IsNaN(ctx.Snapshot.InteractionSurface.Crowding)
                    ? 0.5 : ctx.Snapshot.InteractionSurface.Crowding;

                // All three dimensions are centred at 0.5 (population average → zero net change).
                // This ensures null DualControl == SexualResponsiveness.Default in behaviour.

                // SES: excitation — above 0.5 raises NeedIntimacy, below 0.5 suppresses it
                var sesBoost = (dcm.SES - 0.5) * Config.SESNeedIntimacyBoostPerHour * h;

                // SIS1: performance/failure inhibition — only above population average (>0.5) inhibits.
                // × 2.0 so that SIS1=1.0 produces the same magnitude as SIS1=0.5 in the old formula.
                var sis1Penalty = Math.Max(0, (dcm.SIS1 - 0.5)) * 2.0 * (s.Stress / 100.0) * Config.SIS1StressInhibitionWeight * h;

                // SIS2: threat/context inhibition — only above population average (>0.5) inhibits.
                var sis2Penalty = Math.Max(0, (dcm.SIS2 - 0.5)) * 2.0 * crowding * Config.SIS2CrowdingInhibitionWeight * h;

                var dcmDelta = sesBoost - sis1Penalty - sis2Penalty;
                if (Math.Abs(dcmDelta) > 0.001)
                {
                    var dcmNext = dcmMotiv with
                    {
                        NeedIntimacy = Math.Clamp(dcmMotiv.NeedIntimacy + dcmDelta, 0, 100)
                    };
                    s = s with { Motivations = dcmNext };
                    outbox.Add(new MotivationChanged(now, ctx.Id, dcmMotiv, dcmNext));
                }
            }

            // ── E1 + E4: Proxemics zone violation → stress (Altman 1975) ────────────────
            // Intimate zone (<0.45m) without privacy is acutely stressful.
            // stressGrowthMult (Neuroticism) is applied — high-N characters are more sensitive.
            {
                var dist = ctx.Snapshot.InteractionSurface.ProxemicDistanceMeters;
                if (dist.HasValue && !double.IsNaN(dist.Value))
                {
                    var zone = Interactions.ProxemicsHelper.GetZone(dist.Value);
                    if (Interactions.ProxemicsHelper.IsZoneViolation(zone, ctx.Snapshot.InteractionSurface.HasPrivacy, ctx.Snapshot.InteractionSurface.Kind))
                    {
                        var zoneStress = zone == Interactions.ProxemicsZone.Intimate
                            ? Config.ProxemicsIntimateZoneStressPerHour
                            : Config.ProxemicsPersonalZoneStressPerHour;
                        s = s with { Stress = Clamp01p(s.Stress + zoneStress * stressGrowthMult * h) };
                    }
                }
            }

            // ── E3 + E4: Privacy non-monotonicity → stress ───────────────────────────────
            // Altman (1975): desired privacy is a function of personality.
            // Two asymmetric mechanisms:
            //   • Crowding stress: actual < desired → stress for introverts in public (always).
            //   • Isolation stress: actual > desired → stress only for E > 0.6 (Altman non-monotonic).
            // Recovery bonus: quiet private space accelerates stress recovery (Kaplan 1995).
            // Only applies when location is known — prevents spurious stress in test contexts.
            // stressGrowthMult (Neuroticism) amplifies crowding sensitivity.
            {
                var surface = ctx.Snapshot.InteractionSurface;
                if (surface.Kind != Interactions.SurfaceKind.Unknown && surface.Location != null)
                {
                    var e = ctx.Personality.BigFive.Extraversion;
                    // desiredPrivacy ∈ [0.2, 0.8]: E=0.1→0.74, E=0.5→0.50, E=0.9→0.26
                    var desiredPrivacy = 0.5 - (e - 0.5) * 0.60;
                    var actualPrivacy = surface.HasPrivacy ? 0.8 : 0.2;

                    // Crowding: actual < desired (too little privacy)
                    var crowdingDeficit = Math.Max(0.0, desiredPrivacy - actualPrivacy);
                    var crowdingStress = crowdingDeficit * Config.PrivacyMismatchStressWeight * stressGrowthMult * h;

                    // Isolation: actual > desired, only when E > 0.6 (extraverts feel lonely alone)
                    var privacyExcess = Math.Max(0.0, actualPrivacy - desiredPrivacy);
                    var isolationStress = e > 0.6
                        ? privacyExcess * e * Config.IsolationStressWeight * h
                        : 0.0;

                    // Recovery bonus: quiet private space → stress decays faster
                    var recoveryBonus = surface.HasPrivacy && surface.Noise < 0.3
                        ? Config.PrivacyRecoveryBonusPerHour * h
                        : 0.0;

                    s = s with { Stress = Clamp01p(s.Stress + crowdingStress + isolationStress - recoveryBonus) };
                }
            }

            // ── E5: Noise → stress (HPA activation via auditory startle cascade) ──────────
            // Glass & Singer 1972; WHO community noise guidelines (55/70/80 dB thresholds).
            // Uncontrollable noise (unknown/public space) → full stress contribution.
            // Home territory (Identity.HomeLocationId matches surface.Location) → 0.4× reduction:
            // the character has agency over the noise source, dramatically lowering cortisol response.
            // Neuroticism (stressGrowthMult) amplifies individual noise sensitivity.
            {
                var surface = ctx.Snapshot.InteractionSurface;
                if (surface.Noise > Config.NoiseStressThreshold
                    && surface.Kind != Interactions.SurfaceKind.Unknown
                    && surface.Location != null)
                {
                    var noiseExcess = surface.Noise - Config.NoiseStressThreshold;
                    var noiseStress = noiseExcess * Config.NoiseStressWeightPerHour * stressGrowthMult * h;

                    // Home territory: controllable noise → 60 % stress reduction
                    var isHome = ctx.Identity.HomeLocationId is { } homeId
                                 && homeId == surface.Location;
                    if (isHome) noiseStress *= Config.HomeNoiseStressMultiplier;

                    s = s with { Stress = Clamp01p(s.Stress + noiseStress) };
                }
            }

            // SAM systém → PAD (Sympatho-Adrenomedullary: okamžitá sympatická aktivace)
            if (ph.AcuteArousalLevel > 0)
            {
                var samContrib = ph.AcuteArousalLevel / 100.0 * Config.AcuteArousalPsychWeight;
                s = s with
                {
                    Arousal = Clamp01(s.Arousal + samContrib * h * 2.0),
                    Dominance = Clamp01(s.Dominance + samContrib * 0.1 * h)
                };
            }

            // Fyzická únava → PAD: přetížení = Valence↓; mírná exerce = stress buffer (Stubbs 2017)
            if (ph.PhysicalFatigueLevel > Config.PhysicalFatigueHighThreshold)
            {
                var excess = (ph.PhysicalFatigueLevel - Config.PhysicalFatigueHighThreshold) / 30.0;
                s = s with
                {
                    Valence = Clampm1p1(s.Valence - excess * Config.PhysicalFatigueValencePenalty * h),
                    Arousal = Clamp01(s.Arousal - excess * Config.PhysicalFatigueArousalPenalty * h)
                };
            }
            else if (ph.PhysicalFatigueLevel > Config.PhysicalFatigueMildThreshold)
            {
                // Mírná fyzická aktivita = endorfiny → snižuje stres
                s = s with { Stress = Math.Max(0, s.Stress - Config.PhysicalFatigueStressReliefWeight * h) };
            }

            // Glykemický stav: hypoglykémie → iritabilita + CogLoad↑
            if (ph.Nutrition is { } glycemicNut && glycemicNut.BloodGlucoseLevel < Config.HypoglycemiaThreshold)
            {
                var hypSeverity = (Config.HypoglycemiaThreshold - glycemicNut.BloodGlucoseLevel) / Config.HypoglycemiaThreshold;
                s = s with
                {
                    Valence = Clampm1p1(s.Valence - hypSeverity * Config.HypoglycemiaValencePenalty * h),
                    CognitiveLoad = Clamp01p(s.CognitiveLoad + hypSeverity * Config.HypoglycemiaCogLoadBonus * h)
                };
            }

            // Dehydratace → kognitivní deficit (Masento 2014): 2% ztráta tělesné vody = zhoršená pracovní paměť
            if (ph.Thirst > Config.DehydrationCogLoadThreshold)
            {
                var dehydSeverity = (ph.Thirst - Config.DehydrationCogLoadThreshold) / 50.0;
                s = s with { CognitiveLoad = Clamp01p(s.CognitiveLoad + dehydSeverity * Config.DehydrationCogLoadBonus * h) };
            }

            // Chronická bolest → depresivní profil (Dantzer 2008): po 7+ dnech s bolestí → Valence↓, MoodBaseline erose
            if (ph.ChronicPainDays > Config.ChronicPainOnsetDays)
            {
                var chronicity = Math.Min(ph.ChronicPainDays / 30.0, 1.0); // nasycení za 30 dní
                s = s with
                {
                    Valence = Clampm1p1(s.Valence - chronicity * Config.ChronicPainValencePenaltyPerDay * h),
                    MoodBaseline = Math.Clamp(s.MoodBaseline - chronicity * Config.ChronicPainMoodBaselinePenaltyPerDay * h, 0, 100)
                };
            }

            // Yerkes-Dodson: optimální kortizolové pásmo mírně zlepšuje kognici (Lupien 2007)
            if (ph.CortisolLevel >= Config.CortisolOptimalLow && ph.CortisolLevel <= Config.CortisolOptimalHigh)
                s = s with { CognitiveLoad = Clamp01p(s.CognitiveLoad - Config.CortisolOptimalCogBonus * h) };

            // PMDD: závažnější psychologické efekty v luteální fázi u postav s vysokým PmsRisk
            if (ph.Cycle?.PmddActive == true)
            {
                s = s with
                {
                    Valence = Clampm1p1(s.Valence - Config.PmddValencePenaltyPerHour * h),
                    Stress = Clamp01p(s.Stress + Config.PmddStressBonus * h)
                };
            }

            // Postpartum hormonální crash: estrogen/progesteron propad → emocionální labilita
            if (ph.Postpartum?.HormonalCrashActive == true)
            {
                var labilityNoise = RandomSym() * Config.PostpartumCrashValenceLability;
                s = s with
                {
                    Valence = Clampm1p1(s.Valence + labilityNoise * h),
                    MoodBaseline = Math.Clamp(s.MoodBaseline - Config.PostpartumCrashMoodBaselinePenalty * h, 0, 100)
                };
            }

            // Ambientní teplota → PAD (Anderson 2002, General Aggression Model)
            {
                var ambientTemp = ctx.Snapshot.AmbientTemperature;
                if (ambientTemp > Config.AmbientTempHeatThreshold)
                {
                    var heatExcess = ambientTemp - Config.AmbientTempHeatThreshold;
                    s = s with
                    {
                        Valence = Clampm1p1(s.Valence - heatExcess * Config.AmbientTempHeatValencePenalty * h),
                        Arousal = Clamp01(s.Arousal + heatExcess * Config.AmbientTempHeatArousalBonus * h)
                    };
                }
                else if (ambientTemp < Config.AmbientTempColdThreshold && s.Motivations is { } coldMotiv)
                {
                    // Mírný chlad → affiliativní hledání tepla/blízkosti (Fay & Maner 2012)
                    var coldFactor = (Config.AmbientTempColdThreshold - ambientTemp) / 10.0;
                    var next = coldMotiv with
                    { NeedSocial = Math.Min(100, coldMotiv.NeedSocial + coldFactor * Config.AmbientTempColdSocialBonus * h) };
                    if (next != coldMotiv)
                    {
                        s = s with { Motivations = next };
                        outbox.Add(new MotivationChanged(now, ctx.Id, coldMotiv, next));
                    }
                }
            }

            // Kognitivní stárnutí + percepce (Salthouse 2009; Gates & Cooper 1991)
            if (ph.Aging is { AgeYears: > 0 } ageState)
            {
                // Kognitivní stárnutí: pracovní paměť a rychlost zpracování klesají po 60
                if (ageState.AgeYears > Config.CognitivAgingThreshold)
                {
                    var cogDecline = (ageState.AgeYears - Config.CognitivAgingThreshold)
                                    * Config.CognitiveAgingCogLoadPerYear * h / (365.25 * 24);
                    s = s with { CognitiveLoad = Clamp01p(s.CognitiveLoad + cogDecline) };
                }
                // Presbyopie/presbyakusis: percepční obtíže zvyšují kognitivní zátěž po 50
                if (ageState.AgeYears > Config.PerceptualAgingThreshold)
                    s = s with { CognitiveLoad = Clamp01p(s.CognitiveLoad + Config.PerceptualAgingCogLoadPerHour * h) };
            }

            // Post-menopauza: estrogen deficience → MoodBaseline erose (serotonin↓, vliv na náladu)
            {
                var isPostMenopausal = ph.Cycle?.Phase == CyclePhase.Paused
                                    && ph.Pregnancy is null
                                    && (ph.Aging?.AgeYears ?? 0) >= 45;
                if (isPostMenopausal)
                    s = s with { MoodBaseline = Math.Clamp(s.MoodBaseline - Config.PostMenopauseMoodBaselinePenaltyPerHour * h, 0, 100) };
            }

            // Altitude → kognitivní deficit (hypoxie mozku)
            {
                var alt = ctx.Snapshot.AltitudeMeters;
                if (alt > Config.AltitudeCogLoadThreshold)
                {
                    var kmAbove = (alt - Config.AltitudeCogLoadThreshold) / 1000.0;
                    s = s with { CognitiveLoad = Clamp01p(s.CognitiveLoad + Config.AltitudeCogLoadBonusPerKm * kmAbove * h) };
                }
            }

            // MoodBaseline — pomalý drift směrem k neutrálu (50), potlačený vysokým stresem
            {
                var moodRecovery = Config.MoodBaselineRecoveryPerHour;
                if (s.Stress > Config.MoodBaselineHighStressThreshold) moodRecovery *= 0.1;
                moodRecovery *= 1.0 + ctx.Personality.BigFive.Agreeableness * Config.MoodBaselineAgreeablenessBonus;
                var alloMoodDampFactor = 1.0 - alloLoad / 200.0;
                // Serotonin IDO pathway (Dantzer 2007): chronická imunita tlumí MoodBaseline recovery
                var serotoninFactor = ph.ImmuneLoad > Config.SerotoninSuppressionImmuneThreshold
                    ? Config.SerotoninMoodRecoveryDampening
                    : 1.0;
                moodRecovery *= serotoninFactor;
                s = s with { MoodBaseline = Math.Clamp(Approach(s.MoodBaseline, 50, moodRecovery * alloMoodDampFactor * h), 0, 100) };
            }

            // Cirkadiánní rytmus — dvě Gaussovy křivky (ráno + večer) s poobědovým poklesem.
            // Vrcholy jsou posunuty o CircadianPhaseShiftHours (chronotyp + jet-lag) z Physiology.
            if (Config.EnableCircadianRhythm)
            {
                var hoursOfDay = (double)(now.Hour % WWorld.Spec.HoursPerDay);
                var phaseShift = ph.CircadianPhaseShiftHours;
                var morningPeak = 0.35 * Math.Exp(-Math.Pow(hoursOfDay - 10.0 - phaseShift, 2) / 16.0);  // σ²=8, peak 10h ± posun
                var eveningPeak = 0.25 * Math.Exp(-Math.Pow(hoursOfDay - 19.0 - phaseShift, 2) / 12.0);  // σ²=6, peak 19h ± posun
                var lunchDip = 0.20 * Math.Exp(-Math.Pow(hoursOfDay - 15.0 - phaseShift, 2) / 3.0);   // σ²=1.5, dip 15h ± posun
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
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
                {
                    _log.EmotionTransition(ctx.Id.Value.ToString(),
                        s.DominantEmotion.ToString(), newDom.ToString(),
                        s.Valence, s.Arousal, s.Stress);
                }
                s = s with { DominantEmotion = newDom };
                outbox.Add(new EmotionShifted(now, ctx.Id, newDom, s.Valence, s.Arousal, s.Dominance));
            }

            var moodDelta = s.MoodBaseline - State.MoodBaseline;
            if (Math.Abs(moodDelta) > 5.0)
            {
                using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
                {
                    _log.MoodBaselineShifted(ctx.Id.Value.ToString(), State.MoodBaseline, s.MoodBaseline, moodDelta);
                }
            }

            foreach (var threshold in new[] { 60.0, 80.0 })
            {
                if (_previousAllostaticLoad < threshold && ph.AllostaticLoad >= threshold)
                {
                    using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
                    {
                        _log.AllostaticLoadMilestone(ctx.Id.Value.ToString(),
                            threshold, _previousAllostaticLoad, ph.AllostaticLoad, ph.CortisolLevel);
                    }
                }
            }
            _previousAllostaticLoad = ph.AllostaticLoad;

            State = s;

            double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
            double Clamp01p(double v) => Math.Max(0, Math.Min(100, v));
            double Clampm1p1(double v) => Math.Max(-1, Math.Min(1, v));
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
                            Valence = Math.Clamp(s.Valence - 0.08 * hungerNorm, -1, 1)
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
                        // Continuous ECR-R model (Brennan et al. 1998):
                        // Anxiety amplifies rejection impact (hyperactivation);
                        // Avoidance suppresses it (deactivation strategy).
                        var attachmentModifier = ctx.Personality.Attachment.Anxiety * 0.12
                                               - ctx.Personality.Attachment.Avoidance * 0.02;
                        var actSensitivity = io.Act switch
                        {
                            SpeechAct.SelfDisclosure => 1.6,
                            SpeechAct.Invite => 1.4,
                            SpeechAct.Validation => 1.2,
                            SpeechAct.Meta => 1.1,
                            _ => 1.0
                        };
                        var impact = (0.05 + 0.10 * n + attachmentModifier) * actSensitivity;
                        // Williams (2007) 4-need threat model: rejection threatens Belonging →
                        // need becomes more urgent, not less (hyperactivation for anxious attachment;
                        // deactivation for avoidant — Mikulincer & Shaver 2016).
                        // RejectionSensitivity proxy: Attachment.Anxiety (Downey & Feldman 1996).
                        var belongingBoost = (4.0 + ctx.Personality.Attachment.Anxiety * 6.0)
                                           * (1.0 - ctx.Personality.Attachment.Avoidance * 0.70);
                        var prevMotivRej = s.Motivations ?? new MotivationState();
                        s = s with
                        {
                            Valence = Math.Max(-1, s.Valence - impact),
                            Stress = Math.Min(100, s.Stress + 3 + 5 * n),
                            Dominance = Math.Clamp(s.Dominance - 0.04 * actSensitivity, 0, 1),
                            MoodBaseline = Math.Clamp(s.MoodBaseline - 8.0, 0, 100),
                            Motivations = prevMotivRej with
                            {
                                NeedSocial = Math.Clamp(prevMotivRej.NeedSocial + belongingBoost, 0, 100),
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
                                Valence = Math.Clamp(s.Valence - 0.08 * hungerNorm, -1, 1)
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

                case Characters.Engines.Interactions.SexualEncounterOutcome se when se.Accepted && (se.From == ctx.Id || se.To == ctx.Id):
                    {
                        // Post-coital reward: liking (opioid system) — valence boost, tension release,
                        // NeedIntimacy satisfied. Wanting (dopamine) recovers via normal physiology tick.
                        // Reference: Dual Control Model (Bancroft & Janssen 2009); Basson 2001.
                        var prevMotivSex = s.Motivations ?? new MotivationState();
                        var liking = 0.07 + (ctx.Personality.DualControl?.SES ?? 0.5) * 0.06;
                        s = s with
                        {
                            Valence = Math.Clamp(s.Valence + liking, -1, 1),
                            Arousal = Math.Clamp(s.Arousal - 0.12, 0, 1),   // post-coital relaxation
                            Stress = Math.Max(0, s.Stress - 5.0),              // tension release
                            Motivations = prevMotivSex with
                            {
                                // NeedIntimacy satisfied — recovers at normal SES-driven rate in Behavior
                                NeedIntimacy = Math.Max(0, prevMotivSex.NeedIntimacy - 30.0)
                            }
                        };
                        if (s.Motivations != prevMotivSex)
                            outbox.Add(new MotivationChanged(se.OccurredAt, ctx.Id, prevMotivSex, s.Motivations!));
                        break;
                    }

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
                                NeedCare = Math.Clamp(prevMotivPd.NeedCare + 15.0, 0, 100),
                                NeedIntimacy = Math.Clamp(prevMotivPd.NeedIntimacy - 5.0, 0, 100)
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
                        Valence = Math.Clamp(s.Valence + 0.25, -1, 1),
                        Arousal = Math.Clamp(s.Arousal + 0.15, 0, 1),
                        Dominance = Math.Clamp(s.Dominance - 0.10, 0, 1),
                        Stress = Math.Clamp(s.Stress - 10, 0, 100),
                        Motivations = prevMotivCb with
                        {
                            NeedCare = Math.Clamp(prevMotivCb.NeedCare + 20.0, 0, 100),
                            NeedIntimacy = Math.Clamp(prevMotivCb.NeedIntimacy - 10.0, 0, 100)
                        }
                    };
                    if (s.Motivations != prevMotivCb)
                        outbox.Add(new MotivationChanged(cb.OccurredAt, ctx.Id, prevMotivCb, s.Motivations!));
                    // Postpartum hormonální crash: radost z porodu + hormonální labilita koexistují
                    if (ph.Postpartum?.HormonalCrashActive == true)
                        s = s with { MoodBaseline = Math.Clamp(s.MoodBaseline - 5.0, 0, 100) };
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

                // ── RejectionNeedsThreat — Williams' 4-need threat model ────────────────
                // Hartgerink et al. 2015 (k=120 Cyberball studies, d > |1.4|):
                // Intimate rejection simultaneously threatens Belonging, Self-esteem,
                // Control, and Meaningful Existence — even brief exclusion is deeply aversive.
                case Characters.Engines.Relationships.RejectionNeedsThreat rnt
                    when rnt.Rejected == ctx.Id:
                    {
                        var intensity = Math.Clamp(rnt.Intensity, 0.72, 1.6);
                        var intimacyScale = rnt.IsIntimateAdvance ? 1.0 : 0.6;

                        // Need 2: Self-esteem — Valence drop + MoodBaseline erosion
                        var valenceDrop = 0.07 * intensity * intimacyScale;
                        var moodDrop = 6.0 * intensity * intimacyScale;

                        // Need 4: Meaningful existence — Stress spike (HPA activation)
                        var stressGain = 5.0 * intensity * intimacyScale;

                        // Need 3: Control — Dominance penalty
                        var dominanceDrop = 0.05 * intensity * intimacyScale;

                        var prevMotivRnt = s.Motivations ?? new MotivationState();
                        s = s with
                        {
                            Valence = Math.Clamp(s.Valence - valenceDrop, -1, 1),
                            MoodBaseline = Math.Clamp(s.MoodBaseline - moodDrop, 0, 100),
                            Stress = Math.Clamp(s.Stress + stressGain, 0, 100),
                            Dominance = Math.Clamp(s.Dominance - dominanceDrop, 0, 1),
                            // Need 1: Belonging — NeedSocial urgency increases (desire to reconnect)
                            Motivations = prevMotivRnt with
                            {
                                NeedSocial = Math.Clamp(prevMotivRnt.NeedSocial + 8.0 * intensity * intimacyScale, 0, 100),
                                NeedSafety = Math.Clamp(prevMotivRnt.NeedSafety + 4.0 * intensity * intimacyScale, 0, 100)
                            }
                        };

                        if (s.Motivations != prevMotivRnt)
                            outbox.Add(new MotivationChanged(rnt.OccurredAt, ctx.Id, prevMotivRnt, s.Motivations!));

                        if (s.Stress > 70 && State.Stress <= 70)
                            outbox.Add(new StressSpiked(rnt.OccurredAt, ctx.Id, s.Stress));

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

                case Characters.Engines.Interactions.NormViolationOccurred nv when nv.Actor == ctx.Id:
                    s = HandleNormViolation(nv, s, ctx, outbox);
                    break;

                case Characters.Engines.Interactions.ObserverNormReaction onr:
                    s = HandleObserverNormReaction(onr, s, ctx, outbox);
                    break;

                case ValueCongruenceViolated vcv when vcv.Actor == ctx.Id:
                    s = HandleValueCongruenceViolated(vcv, s, ctx, outbox);
                    break;

                // Object affordance applied via UseInPlace (AffordanceApplicationService).
                // Physiology handles Hunger/Thirst — Psychology owns the affective layer:
                // MoodBoost, Warmth, Social, and StressRaise.
                case Objects.ObjectAffordanceApplied oaa when oaa.Actor == ctx.Id:
                    s = ApplyObjectAffordance(s, oaa, ctx);
                    break;
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
        /// Returns the per-emotion valence decay rate multiplier (Verduyn &amp; Lavrijsen 2015).
        /// Values below 1.0 slow decay (emotion lingers); values above 1.0 accelerate it (emotion fades fast).
        /// </summary>
        private static double GetEmotionDecayMultiplier(DiscreteEmotion emotion, PsychologyConfig cfg)
            => emotion switch
            {
                DiscreteEmotion.Fear => cfg.EmotionDecayFear,
                DiscreteEmotion.Surprise => cfg.EmotionDecaySurprise,
                DiscreteEmotion.Disgust => cfg.EmotionDecayDisgust,
                DiscreteEmotion.Joy => cfg.EmotionDecayJoy,
                DiscreteEmotion.Pride => cfg.EmotionDecayPride,
                DiscreteEmotion.Tenderness => cfg.EmotionDecayTenderness,
                DiscreteEmotion.Anger => cfg.EmotionDecayAnger,
                DiscreteEmotion.Shame => cfg.EmotionDecayShame,
                DiscreteEmotion.Guilt => cfg.EmotionDecayGuilt,
                DiscreteEmotion.Sadness => cfg.EmotionDecaySadness,
                _ => 1.0
            };

        /// <summary>
        /// Infers the dominant discrete emotion from the continuous PAD state.
        /// Extracted from Tick() local function to be callable from Handle-phase handlers.
        /// </summary>
        private static DiscreteEmotion InferEmotion(PsychologyState ps)
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

            // Guilt — negativní + střední dominance + zvýšený arousal (approach-motivated, reparativní)
            // Klíčový rozdíl od Shame: vyšší Dominance (0.30–0.55) = postava není paralyzovaná, chce napravit.
            // VAD: V<-0.35, D∈[0.25,0.55], A>0.35
            // Zdroj: Tangney & Dearing (2002); Singh & Bhushan (2025, PMC12647085).
            if (ps.Valence < -0.35 && ps.Dominance is >= 0.25 and <= 0.55 && ps.Arousal > 0.35 && ps.Stress < 50)
                return DiscreteEmotion.Guilt;

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

        /// <summary>
        /// Applies a shame spike to the actor's psychology when they commit a norm-violating action.
        /// </summary>
        /// <remarks>
        /// VAD signature is distinct from VAD-emergent shame (InferEmotion threshold):
        /// <list type="bullet">
        ///   <item>Stronger Dominance drop — identity-level devaluation (Sznycer 2016).</item>
        ///   <item>Arousal modulated by audience presence (Dickerson 2004 vs. Gruenewald 2004).</item>
        ///   <item>Personality scaling: Neuroticism gain, Extraversion damping (Muris et al. 2018).</item>
        /// </list>
        /// The existing <see cref="PsychologyConfig.EmotionDecayShame"/> governs decay — no new decay rate needed.
        /// </remarks>
        private PsychologyState HandleNormViolation(
            Characters.Engines.Interactions.NormViolationOccurred nv,
            PsychologyState s,
            IHumanContext ctx,
            IEventCollector outbox)
        {
            if (nv.ViolationScore < Config.NormShameMinViolationScore)
                return s;

            var (dv, da, dd) = Characters.Engines.Interactions.NormViolationMath.ComputeShameSpike(
                nv.ViolationScore,
                nv.HasAudience,
                ctx.Personality);

            s = s with
            {
                Valence = Math.Clamp(s.Valence + dv, -1.0, 1.0),
                Arousal = Math.Clamp(s.Arousal + da, 0.0, 1.0),
                Dominance = Math.Clamp(s.Dominance + dd, 0.0, 1.0)
            };

            // Force emotion inference — Shame should appear when the spike is large enough.
            var newEmotion = InferEmotion(s);
            if (newEmotion != s.DominantEmotion)
            {
                s = s with { DominantEmotion = newEmotion };
                outbox.Add(new EmotionShifted(nv.OccurredAt, ctx.Id, newEmotion, s.Valence, s.Arousal, s.Dominance));
            }

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
            {
                _log.NormViolationShameSpiked(
                    ctx.Id.Value.ToString(),
                    nv.NormKind.ToString(),
                    nv.ViolationScore,
                    nv.HasAudience);
            }

            return s;
        }

        private PsychologyState HandleObserverNormReaction(
            Characters.Engines.Interactions.ObserverNormReaction onr,
            PsychologyState s,
            IHumanContext ctx,
            IEventCollector outbox)
        {
            if (onr.ViolationScore < Config.NormShameMinViolationScore)
                return s;

            var n = ctx.Personality.BigFive.Neuroticism;
            var attachment = ctx.Personality.Attachment;

            // Route by reaction kind with distinct VAD signatures
            var (dv, da, dd, stressDelta) = onr.ReactionKind switch
            {
                Characters.Engines.Interactions.ObserverReactionKind.Anger =>
                    ComputeAngerResponse(onr.ViolationScore, n),

                Characters.Engines.Interactions.ObserverReactionKind.MoralOutrage =>
                    ComputeOutrageResponse(onr.ViolationScore, n),

                Characters.Engines.Interactions.ObserverReactionKind.VicariousShame =>
                    ComputeVicariousShameResponse(onr.ViolationScore, attachment),

                _ => (0.0, 0.0, 0.0, 0.0)
            };

            // Apply violation score scaling
            dv *= onr.ViolationScore;
            da *= onr.ViolationScore;
            dd *= onr.ViolationScore;
            stressDelta *= onr.ViolationScore;

            s = s with
            {
                Valence = Math.Clamp(s.Valence + dv, -1.0, 1.0),
                Arousal = Math.Clamp(s.Arousal + da, 0.0, 1.0),
                Dominance = Math.Clamp(s.Dominance + dd, 0.0, 1.0),
                Stress = Math.Clamp(s.Stress + stressDelta, 0.0, 100.0)
            };

            // Force emotion inference
            var newEmotion = InferEmotion(s);
            if (newEmotion != s.DominantEmotion)
            {
                s = s with { DominantEmotion = newEmotion };
                outbox.Add(new EmotionShifted(onr.OccurredAt, ctx.Id, newEmotion, s.Valence, s.Arousal, s.Dominance));
            }

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
            {
                _log.ObserverNormReactionRouted(
                    ctx.Id.Value.ToString(),
                    onr.ReactionKind.ToString(),
                    onr.NormKind.ToString(),
                    onr.ViolationScore);
            }

            return s;
        }

        private static (double deltaValence, double deltaArousal, double deltaDominance, double stressDelta)
        ComputeAngerResponse(double violationScore, double neuroticism)
        {
            // Anger is approach-motivated: mild dominance drop (confrontational readiness)
            // High neuroticism amplifies stress response
            var dv = -0.40;  // -0.30 to -0.50 range
            var da = +0.28;  // +0.20 to +0.35 range
            var dd = -0.32;  // -0.25 to -0.40 range
            var stressDelta = 7.0 + neuroticism * 3.0;  // 7–10 range

            return (dv, da, dd, stressDelta);
        }

        private static (double deltaValence, double deltaArousal, double deltaDominance, double stressDelta)
        ComputeOutrageResponse(double violationScore, double neuroticism)
        {
            // Moral outrage: strong condemnation, moderate arousal
            // Neuroticism modulation: High-N amplifies × 1.25, Low-N dampens × 0.80
            var neuMult = 1.0 + (neuroticism - 0.5) * 0.50;  // [0.75, 1.25]
            neuMult = Math.Max(0.75, Math.Min(1.25, neuMult));

            var dv = -0.45 * neuMult;  // -0.35 to -0.55 range
            var da = +0.35 * neuMult;  // +0.25 to +0.45 range
            var dd = -0.18;             // -0.10 to -0.25 range (moral authority, less confrontational)
            var stressDelta = (5.0 + neuroticism * 3.0) * neuMult;  // 3–8 range

            return (dv, da, dd, stressDelta);
        }

        /// <summary>
        /// Applies a Guilt spike when the character commits an action that violates their core values.
        /// </summary>
        /// <remarks>
        /// VAD signature for Guilt (distinct from Shame on the Dominance axis):
        /// <list type="bullet">
        ///   <item>Valence: strongly negative (own moral failure).</item>
        ///   <item>Arousal: elevated — Guilt is approach-motivated, not paralysing.</item>
        ///   <item>Dominance: moderately low but distinctly above Shame — character retains agency to repair.</item>
        /// </list>
        /// Big Five coupling for Guilt (Muris et al. 2018, PMC5856863):
        /// Agreeableness r≈.29 and Conscientiousness r≈.21 amplify guilt-proneness (moral traits, not affective).
        /// Source: Tangney &amp; Dearing (2002); Frontiers in Psychology systematic review (2025, PMC12647085).
        /// </remarks>
        private PsychologyState HandleValueCongruenceViolated(
            ValueCongruenceViolated vcv,
            PsychologyState s,
            IHumanContext ctx,
            IEventCollector outbox)
        {
            // Congruence is in [−1..0] when this handler fires (threshold was < 0).
            var violationMagnitude = Math.Abs(vcv.Congruence);

            var a = ctx.Personality.BigFive.Agreeableness;
            var c = ctx.Personality.BigFive.Conscientiousness;

            // Personality multiplier calibrated to Muris et al. (2018) effect sizes.
            // Agreeableness and Conscientiousness are the guilt predictors (not Neuroticism).
            var personalityMult = 1.0
                + 0.29 * (a - 0.5)   // Agreeableness gain (r ≈ .29)
                + 0.21 * (c - 0.5);  // Conscientiousness gain (r ≈ .21)
            personalityMult = Math.Clamp(personalityMult, 0.40, 1.80);

            // VAD deltas: V strongly negative, A elevated (approach), D moderately low.
            var dv = Math.Clamp(-0.55 * violationMagnitude * personalityMult, -0.75, 0.0);
            var da = Math.Clamp( 0.45 * violationMagnitude * personalityMult,  0.0,  0.70);
            var dd = Math.Clamp(-0.20 * violationMagnitude * personalityMult, -0.45, 0.0);

            s = s with
            {
                Valence   = Math.Clamp(s.Valence   + dv, -1.0, 1.0),
                Arousal   = Math.Clamp(s.Arousal   + da,  0.0, 1.0),
                Dominance = Math.Clamp(s.Dominance + dd,  0.0, 1.0)
            };

            var newEmotion = InferEmotion(s);
            if (newEmotion != s.DominantEmotion)
            {
                s = s with { DominantEmotion = newEmotion };
                outbox.Add(new EmotionShifted(vcv.OccurredAt, ctx.Id, newEmotion, s.Valence, s.Arousal, s.Dominance));
            }

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultPsychologyEngine)))
            {
                _log.GuiltSpikeApplied(
                    ctx.Id.Value.ToString(),
                    vcv.ActionName,
                    vcv.DominantViolatedValue,
                    vcv.Congruence,
                    dv);
            }

            return s;
        }

        private static (double deltaValence, double deltaArousal, double deltaDominance, double stressDelta)
        ComputeVicariousShameResponse(double violationScore, Traits.AttachmentProfile attachment)
        {
            // Vicarious shame: empathetic response, group identity threatened
            // Attachment modulation: Anxious amplifies × 1.40, Avoidant dampens × 0.65
            var attachMult = 1.0 + (attachment.Anxiety - attachment.Avoidance) * 0.40;
            attachMult = Math.Max(0.65, Math.Min(1.40, attachMult));

            var dv = -0.32 * attachMult;  // -0.25 to -0.40 range
            var da = +0.15 * attachMult;  // +0.10 to +0.20 range
            var dd = -0.43 * attachMult;  // -0.35 to -0.50 range (group status threatened)
            var stressDelta = (3.0 + attachment.Anxiety * 2.0) * attachMult;  // 2–5 range

            return (dv, da, dd, stressDelta);
        }

        /// <summary>
        /// Replaces the current state with the provided snapshot.
        /// Used by the persistence layer after a save/load cycle and by tests to establish
        /// specific initial psychological conditions.
        /// </summary>
        /// <param name="state">The psychology state to restore.</param>
        public void RestoreState(PsychologyState state) => State = state;

        #region Object affordance application

        /// <summary>
        /// Applies the psychological effect of a single object affordance event.
        /// Hunger and Thirst are handled by <c>DefaultPhysiologyEngine</c> — this method
        /// covers the affective layer only.
        /// </summary>
        /// <param name="s">Current psychology state.</param>
        /// <param name="oaa">Affordance event carrying type and satisfaction [0..1].</param>
        /// <param name="ctx">Character context — used for NeedBelonging scaling on Social.</param>
        private PsychologyState ApplyObjectAffordance(
            PsychologyState s,
            Objects.ObjectAffordanceApplied oaa,
            IHumanContext ctx)
            => oaa.AffordanceType switch
            {
                // Pleasant environment — art, candles, flowers, nature sounds.
                // Valence spike is immediate; MoodBaseline shift is small but persistent.
                AffordanceType.MoodBoost => s with
                {
                    Valence      = Math.Clamp(s.Valence + oaa.Satisfaction * Config.AffordanceMoodBoostMaxValence, -1, 1),
                    MoodBaseline = Math.Clamp(s.MoodBaseline + oaa.Satisfaction * Config.AffordanceMoodBoostMaxMoodBaseline, 0, 100)
                },
        
                // Warmth relief — fireplace, hearth, forge.
                // Cold stress is a physiological threat; warmth resolves the drive.
                AffordanceType.Warmth => s with
                {
                    Stress = Math.Clamp(s.Stress - oaa.Satisfaction * Config.AffordanceWarmthMaxStressRelief, 0, 100)
                },
        
                // Communal space — tavern table, campfire, chapel.
                // Effect is need-scaled: lonely characters benefit more (Cacioppo 2008).
                AffordanceType.Social => s with
                {
                    Valence = Math.Clamp(
                        s.Valence + oaa.Satisfaction
                                  * Config.AffordanceSocialMaxValence
                                  * (ctx.Snapshot.Behavior.NeedBelonging / 100.0),
                        -1, 1)
                },
        
                // Hazard / threat — weapons, fire, intimidating objects.
                // Stress spike is immediate; does not affect Valence directly
                // (fear → high Stress → Valence drops naturally via Tick physio modulation).
                AffordanceType.StressRaise => s with
                {
                    Stress = Math.Clamp(s.Stress + oaa.Satisfaction * Config.AffordanceStressRaiseMaxStress, 0, 100)
                },
        
                // Hunger, Thirst, Rest, Work, Entertainment — not psychology concerns at this layer.
                _ => s
            };
        
            #endregion Object affordance application
    }
}
