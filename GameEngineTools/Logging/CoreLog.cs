// CoreLog.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Centrální logovací metody pro GameEngineTools — generovány source generátorem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Všechny metody jsou generovány přes <c>[LoggerMessage]</c> source generator,
    /// který zajišťuje <b>nulové alokace</b> za runtime a compile-time kontrolu parametrů.
    /// </para>
    /// <para>
    /// <b>Konvence EventId rozsahů:</b>
    /// <list type="table">
    ///   <item><term>1000–1999</term><description>Behavior — rozhodování, sleep, interakce</description></item>
    ///   <item><term>2000–2999</term><description>Relationships — vztahy, decay</description></item>
    ///   <item><term>3000–3999</term><description>Memory — epizodická paměť, konsolidace</description></item>
    ///   <item><term>4000–4999</term><description>Infrastruktura — Scheduler, hosting</description></item>
    ///   <item><term>5000–5999</term><description>Snapshoty — Physiology / Psychology / Behavior (diagnostika)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static partial class CoreLog
    {
        #region Physiology

        /// <summary>Snapshot fyziologického stavu postavy — loguje se každý tick.</summary>
        [LoggerMessage(
            EventId = 5000,
            Level = LogLevel.Information,
            Message = "[PHYSIO] {HumanId} Energy:{Energy:F1} Hunger:{Hunger:F2} Thirst:{Thirst:F2} Pain:{Pain:F1} SleepDebt:{SleepDebt:F1}h Temp:{TempDelta:+0.0;-0.0}°C Immune:{Immune:F1}")]
        public static partial void PhysiologySnapshot(
            this ILogger logger,
            string HumanId,
            double Energy,
            double Hunger,
            double Thirst,
            double Pain,
            double SleepDebt,
            double TempDelta,
            double Immune);

        /// <summary>Aktuální fáze menstruačního cyklu postavy.</summary>
        [LoggerMessage(
            EventId = 5001,
            Level = LogLevel.Debug,
            Message = "[PHYSIO/CYCLE] {HumanId} Phase:{Phase} Day:{DayInCycle}")]
        public static partial void PhysiologyCycle(
            this ILogger logger,
            string HumanId,
            string Phase,
            int DayInCycle);

        [LoggerMessage(EventId = 5002,
            Level = LogLevel.Debug,
            Message = "[PHYSIO/SLEEP] {HumanId} SleepEnded — délka: {Hours:F1}h, kvalita: {Quality:F0}, dluh po obnově: {Debt:F2}h.")]
        public static partial void PhysiologySleepEnded(this ILogger logger, string HumanId, double Hours, double Quality, double Debt);

        [LoggerMessage(
            EventId = 5003,
            Level = LogLevel.Debug,
            Message = "[PHYSIO/REPRO] {HumanId} conception evaluated with {OtherParent}: chance={Chance:F3}, ovulation={OvulationWindow}, intent={Intent}, contraception={Contraception}, result={Result}.")]
        public static partial void PhysiologyConceptionEvaluated(
            this ILogger logger,
            string HumanId,
            string OtherParent,
            double Chance,
            bool OvulationWindow,
            string Intent,
            string Contraception,
            string Result);

        [LoggerMessage(
            EventId = 5004,
            Level = LogLevel.Information,
            Message = "[PHYSIO/REPRO] {HumanId} pregnancy started with otherParent={OtherParent}, due={EstimatedDueDate}.")]
        public static partial void PhysiologyPregnancyStarted(
            this ILogger logger,
            string HumanId,
            string OtherParent,
            string EstimatedDueDate);

        [LoggerMessage(
            EventId = 5005,
            Level = LogLevel.Information,
            Message = "[PHYSIO/REPRO] {HumanId} pregnancy discovered, otherParent={OtherParent}, daysPregnant={DaysPregnant}.")]
        public static partial void PhysiologyPregnancyDiscovered(
            this ILogger logger,
            string HumanId,
            string OtherParent,
            long DaysPregnant);

        [LoggerMessage(
            EventId = 5006,
            Level = LogLevel.Information,
            Message = "[PHYSIO/REPRO] {HumanId} child born, otherParent={OtherParent}, conceivedOn={ConceivedOn}, due={EstimatedDueDate}.")]
        public static partial void PhysiologyChildBorn(
            this ILogger logger,
            string HumanId,
            string OtherParent,
            string ConceivedOn,
            string EstimatedDueDate);

        #endregion Physiology

        #region Psychology

        /// <summary>Snapshot psychologického stavu postavy — loguje se každý tick.</summary>
        [LoggerMessage(
            EventId = 5100,
            Level = LogLevel.Information,
            Message = "[PSYCH] {HumanId} Emotion:{Emotion} V:{Valence:+0.00;-0.00} A:{Arousal:F2} D:{Dominance:F2} Stress:{Stress:F1} CogLoad:{CogLoad:F1}")]
        public static partial void PsychologySnapshot(
            this ILogger logger,
            string HumanId,
            string Emotion,
            double Valence,
            double Arousal,
            double Dominance,
            double Stress,
            double CogLoad);

        /// <summary>
        /// Psychologický dopad noční můry — stres vzrostl, valence klesla.
        /// </summary>
        /// <remarks>
        /// Liší se od <see cref="SleepNightmare"/> (1108) — to loguje VÝSKYT noční můry
        /// v <c>DefaultSleepSession</c>. Tato metoda loguje EFEKT na psychiku v
        /// <c>DefaultPsychologyEngine</c>.
        /// </remarks>
        [LoggerMessage(
            EventId = 5101,
            Level = LogLevel.Debug,
            Message = "[PSYCH/NIGHTMARE] {HumanId} Noční můra → stres +{StressSpike:F1}, valence -{ValencePenalty:F2}.")]
        public static partial void PsychNightmareEffect(
            this ILogger logger,
            string HumanId,
            double StressSpike,
            double ValencePenalty);

        [LoggerMessage(
            EventId = 5102,
            Level = LogLevel.Debug,
            Message = "[PSYCH/SLEEP] Nekvalitní spánek (kvalita={Quality:F0}) → stres +{StressDelta:F1}.")]
        public static partial void PsychSleepInterrupted(this ILogger logger, double Quality, double StressDelta);

        /// <summary>Dominant emotion changed — transition with VAD context.</summary>
        [LoggerMessage(
            EventId = 5103,
            Level = LogLevel.Information,
            Message = "[PSYCH/EMOTION] {HumanId} {OldEmotion}->{NewEmotion} V:{Valence:+0.00;-0.00} A:{Arousal:F2} stress={Stress:F1}")]
        public static partial void EmotionTransition(
            this ILogger logger, string HumanId,
            string OldEmotion, string NewEmotion,
            double Valence, double Arousal, double Stress);

        /// <summary>MoodBaseline shifted significantly (delta > 5 points).</summary>
        [LoggerMessage(
            EventId = 5104,
            Level = LogLevel.Debug,
            Message = "[PSYCH/MOOD] {HumanId} MoodBaseline {OldBaseline:F1}->{NewBaseline:F1} (Δ={Delta:+0.0;-0.0})")]
        public static partial void MoodBaselineShifted(
            this ILogger logger, string HumanId,
            double OldBaseline, double NewBaseline, double Delta);

        /// <summary>AllostaticLoad crossed a critical threshold (60 or 80).</summary>
        [LoggerMessage(
            EventId = 5105,
            Level = LogLevel.Information,
            Message = "[PSYCH/ALLOSTATIC] {HumanId} AllostaticLoad crossed {Threshold:F0}: {OldLoad:F1}->{NewLoad:F1} (cortisol={Cortisol:F1})")]
        public static partial void AllostaticLoadMilestone(
            this ILogger logger, string HumanId,
            double Threshold, double OldLoad, double NewLoad, double Cortisol);

        #endregion Psychology

        #region Behavior — snapshoty

        /// <summary>Snapshot behaviorálního stavu — potřeby a aktuální plán.</summary>
        [LoggerMessage(
            EventId = 5200,
            Level = LogLevel.Information,
            Message = "[BEHAV] {HumanId} Plan:{Plan} Rest:{Rest:F1} Food:{Food:F1} Water:{Water:F1} Belonging:{Belonging:F1} Competence:{Competence:F1} Intimacy:{Intimacy:F1}")]
        public static partial void BehaviorSnapshot(
            this ILogger logger,
            string HumanId,
            string Plan,
            double Rest,
            double Food,
            double Water,
            double Belonging,
            double Competence,
            double Intimacy);

        /// <summary>Detail vybrané akce — utility skóre a timing.</summary>
        [LoggerMessage(
            EventId = 5201,
            Level = LogLevel.Debug,
            Message = "[BEHAV/PLAN] {HumanId} Action:{Action} Start:{Start} Duration:{Duration} Utility:{Utility:F3}")]
        public static partial void BehaviorPlan(
            this ILogger logger,
            string HumanId,
            string Action,
            string Start,
            string Duration,
            double Utility);

        #endregion Behavior — snapshoty

        #region Behavior — rozhodování

        /// <summary>Kandidát na akci s vypočtenou utilitou — loguje se při výběru akce.</summary>
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Debug,
            Message = "[BEHAV/CAND] {HumanId} Action:{Action} Utility:{Utility:F3}")]
        public static partial void BehaviorCandidate(
            this ILogger logger,
            string HumanId,
            string Action,
            double Utility);

        /// <summary>Výsledek výběru akce — která akce byla zvolena.</summary>
        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "[BEHAV/DECISION] {HumanId} → {Action} (utility: {Utility:F3})")]
        public static partial void BehaviorDecision(
            this ILogger logger,
            string HumanId,
            string Action,
            double Utility);

        /// <summary>
        /// Akce stále běží — engine ji ponechává, nepřeplánuje.
        /// Loguje se jako Debug, protože se opakuje každý tick po celou dobu trvání akce.
        /// </summary>
        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Debug,
            Message = "[BEHAV/RUNNING] {HumanId} Akce '{Action}' stále běží, zbývá {Remaining}.")]
        public static partial void BehaviorActionRunning(
            this ILogger logger,
            string HumanId,
            string Action,
            string Remaining);

        /// <summary>
        /// Nová akce byla zvolena utility funkcí a commitována do outboxu.
        /// Loguje se jako Information — každá změna akce je důležitá pro debugging chování.
        /// </summary>
        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Information,
            Message = "[BEHAV/CHOSEN] {HumanId} Nová akce: '{Action}' (utility={Utility:F2}, trvání={Duration}).")]
        public static partial void BehaviorActionChosen(
            this ILogger logger,
            string HumanId,
            string Action,
            double Utility,
            string Duration);

        /// <summary>
        /// Cooldown nastaven pro akci — postava ji nemůže opakovat dříve než vyprší.
        /// </summary>
        /// <remarks>
        /// Pozor: <c>SetCooldown</c> nemá přístup k <c>ctx</c>, proto <c>HumanId</c>
        /// musíš předat explicitně jako parametr, nebo metodu přesunout tam, kde ctx existuje.
        /// </remarks>
        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Debug,
            Message = "[BEHAV/COOLDOWN] {HumanId} Cooldown '{Action}': {Hours}h.")]
        public static partial void BehaviorCooldownSet(
            this ILogger logger,
            string HumanId,
            string Action,
            double Hours);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Debug,
            Message = "[BEHAV/INTENT] {HumanId} intent selected {IntentKind} -> {Action} (score={Score:F2}).")]
        public static partial void BehaviorIntentSelected(
            this ILogger logger,
            string HumanId,
            string IntentKind,
            string Action,
            double Score);

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Debug,
            Message = "[BEHAV/INTENT] {HumanId} intent retained {IntentKind} -> {Action} (score={Score:F2}).")]
        public static partial void BehaviorIntentRetained(
            this ILogger logger,
            string HumanId,
            string IntentKind,
            string Action,
            double Score);

        [LoggerMessage(
            EventId = 1007,
            Level = LogLevel.Debug,
            Message = "[BEHAV/INTENT] {HumanId} intent switched {IntentKind} -> {Action} (score={Score:F2}).")]
        public static partial void BehaviorIntentSwitched(
            this ILogger logger,
            string HumanId,
            string IntentKind,
            string Action,
            double Score);

        [LoggerMessage(
            EventId = 1008,
            Level = LogLevel.Debug,
            Message = "[BEHAV/INTENT] {HumanId} intent expired {IntentKind}.")]
        public static partial void BehaviorIntentExpired(
            this ILogger logger,
            string HumanId,
            string IntentKind);

        [LoggerMessage(
            EventId = 1009,
            Level = LogLevel.Debug,
            Message = "[BEHAV/INTENT] {HumanId} emergency override {IntentKind} -> {Action} (score={Score:F2}).")]
        public static partial void BehaviorIntentEmergencyOverride(
            this ILogger logger,
            string HumanId,
            string IntentKind,
            string Action,
            double Score);

        [LoggerMessage(
            EventId = 1010,
            Level = LogLevel.Debug,
            Message = "[BEHAV/INTENT] {HumanId} bias applied to {Action}: +{Bias:F2}.")]
        public static partial void BehaviorIntentBiasApplied(
            this ILogger logger,
            string HumanId,
            string Action,
            double Bias);

        [LoggerMessage(
            EventId = 1011,
            Level = LogLevel.Debug,
            Message = "[BEHAV/HABIT] {HumanId} learned {Action} cue={Cue} surface={Surface} time={TimeBand} strength {BeforeStrength:F3}->{AfterStrength:F3} learn={Learning:F3} cueFit={CueFit:F2} relief={ReliefFit:F2} coping={CopingFit:F2} constraint={ConstraintPenalty:F2} tendency={Tendency} reps={RepetitionCount}.")]
        public static partial void BehaviorHabitLearned(
            this ILogger logger,
            string HumanId,
            string Action,
            string Cue,
            string Surface,
            string TimeBand,
            double BeforeStrength,
            double AfterStrength,
            double Learning,
            double CueFit,
            double ReliefFit,
            double CopingFit,
            double ConstraintPenalty,
            string Tendency,
            int RepetitionCount);

        [LoggerMessage(
            EventId = 1012,
            Level = LogLevel.Debug,
            Message = "[BEHAV/HABIT] {HumanId} decay days={Days:F3} retention={Retention:F4} traces {BeforeCount}->{AfterCount} removed={RemovedCount}.")]
        public static partial void BehaviorHabitDecayed(
            this ILogger logger,
            string HumanId,
            double Days,
            double Retention,
            int BeforeCount,
            int AfterCount,
            int RemovedCount);

        [LoggerMessage(
            EventId = 1013,
            Level = LogLevel.Debug,
            Message = "[BEHAV/HABIT] {HumanId} pruned traces {BeforeCount}->{AfterCount} max={MaxTraces}.")]
        public static partial void BehaviorHabitPruned(
            this ILogger logger,
            string HumanId,
            int BeforeCount,
            int AfterCount,
            int MaxTraces);

        [LoggerMessage(
            EventId = 1014,
            Level = LogLevel.Debug,
            Message = "[BEHAV/HABIT] {HumanId} bias {Action}: utility {BeforeUtility:F3}->{AfterUtility:F3}, applicability={Applicability:F3}, multiplier={Multiplier:F3}, flat={FlatBias:F3}.")]
        public static partial void BehaviorHabitBiasApplied(
            this ILogger logger,
            string HumanId,
            string Action,
            double BeforeUtility,
            double AfterUtility,
            double Applicability,
            double Multiplier,
            double FlatBias);

        /// <summary>Decision dominated by a habit trace (habit contributed >40% of utility).</summary>
        [LoggerMessage(
            EventId = 1015,
            Level = LogLevel.Debug,
            Message = "[BEHAV/HABIT] {HumanId} Habit dominated decision: {Action} (habitBias={HabitBias:F3}, totalUtility={TotalUtility:F3}, ratio={Ratio:F2})")]
        public static partial void HabitDominatedDecision(
            this ILogger logger, string HumanId, string Action,
            double HabitBias, double TotalUtility, double Ratio);

        /// <summary>A behavior need crossed a critical threshold.</summary>
        [LoggerMessage(
            EventId = 1016,
            Level = LogLevel.Information,
            Message = "[BEHAV/NEED] {HumanId} {Need} crossed threshold {Threshold:F0}: {OldValue:F1}->{NewValue:F1}")]
        public static partial void NeedThresholdCrossed(
            this ILogger logger, string HumanId, string Need,
            double Threshold, double OldValue, double NewValue);

        #endregion Behavior — rozhodování

        #region Behavior/Sleep — spánkový subsystém

        /// <summary>Sleep prompt byl odeslán — postava dosáhla prahu únavy.</summary>
        [LoggerMessage(
            EventId = 1100,
            Level = LogLevel.Information,
            Message = "[BEHAV/SLEEP] {HumanId} Sleep prompt vyslán (NeedRest={NeedRest:F1}).")]
        public static partial void SleepPromptSent(
            this ILogger logger,
            string HumanId,
            double NeedRest);

        /// <summary>Spánek potvrzen — session zahájena.</summary>
        [LoggerMessage(
            EventId = 1101,
            Level = LogLevel.Information,
            Message = "[BEHAV/SLEEP] {HumanId} Spánek zahájen. Délka: {Hours:F1}h.")]
        public static partial void SleepStarted(
            this ILogger logger,
            string HumanId,
            double Hours);

        /// <summary>Sdílený spánek zahájen — postava spí s někým dalším.</summary>
        [LoggerMessage(
            EventId = 1102,
            Level = LogLevel.Information,
            Message = "[BEHAV/SLEEP] {HumanId} Zahájil sdílený spánek ({Type}) se {Companion}.")]
        public static partial void SharedSleepStarted(
            this ILogger logger,
            string HumanId,
            string Type,
            string Companion);

        /// <summary>Spánek odmítnut hráčem — grace perioda se zkracuje.</summary>
        [LoggerMessage(
            EventId = 1103,
            Level = LogLevel.Warning,
            Message = "[BEHAV/SLEEP] {HumanId} Spánek odmítnut (#{Count}), grace: {Grace:F1}h.")]
        public static partial void SleepDeclinedByPlayer(
            this ILogger logger,
            string HumanId,
            int Count,
            double Grace);

        /// <summary>Sleep session ukončena přirozeně nebo přerušením.</summary>
        [LoggerMessage(
            EventId = 1104,
            Level = LogLevel.Information,
            Message = "[BEHAV/SLEEP] {HumanId} Sleep session ukončena, cooldown {Cooldown}h.")]
        public static partial void SleepSessionEnded(
            this ILogger logger,
            string HumanId,
            double Cooldown);

        /// <summary>Postava se probudila — délka a kvalita spánku.</summary>
        [LoggerMessage(
            EventId = 1105,
            Level = LogLevel.Information,
            Message = "[BEHAV/SLEEP] {HumanId} Probudil se. Délka: {Hours:F2}h, Kvalita: {Quality:F1}, Přerušen: {WasInterrupted}.")]
        public static partial void SleepWokeUp(
            this ILogger logger,
            string HumanId,
            double Hours,
            double Quality,
            bool WasInterrupted);

        /// <summary>Spánek přerušen — důvod a aktuální fáze.</summary>
        [LoggerMessage(
            EventId = 1106,
            Level = LogLevel.Warning,
            Message = "[BEHAV/SLEEP] {HumanId} Spánek přerušen ({Cause}) ve fázi {Phase}.")]
        public static partial void SleepInterrupted(
            this ILogger logger,
            string HumanId,
            string Cause,
            string Phase);

        /// <summary>Přechod mezi spánkovými fázemi.</summary>
        [LoggerMessage(
            EventId = 1107,
            Level = LogLevel.Debug,
            Message = "[BEHAV/SLEEP] {HumanId} Vstoupil do fáze {Phase}.")]
        public static partial void SleepPhaseEntered(
            this ILogger logger,
            string HumanId,
            string Phase);

        /// <summary>Noční můra — přerušuje REM fázi.</summary>
        [LoggerMessage(
            EventId = 1108,
            Level = LogLevel.Debug,
            Message = "[BEHAV/SLEEP] {HumanId} Noční můra (stres při usnutí: {StressAtSleep:F1}).")]
        public static partial void SleepNightmare(
            this ILogger logger,
            string HumanId,
            double StressAtSleep);

        /// <summary>Sen — REM fáze proběhla klidně.</summary>
        [LoggerMessage(
            EventId = 1109,
            Level = LogLevel.Debug,
            Message = "[BEHAV/SLEEP] {HumanId} Sen (seed: {Seed}).")]
        public static partial void SleepDream(
            this ILogger logger,
            string HumanId,
            int Seed);

        /// <summary>Přepadení během spánku.</summary>
        [LoggerMessage(
            EventId = 1110,
            Level = LogLevel.Warning,
            Message = "[BEHAV/SLEEP] {HumanId} Přepadení během spánku (fáze: {Phase})!")]
        public static partial void SleepAmbush(
            this ILogger logger,
            string HumanId,
            string Phase);

        /// <summary>Penalizace za opakované odmítání spánku — roste stres.</summary>
        [LoggerMessage(
            EventId = 1111,
            Level = LogLevel.Debug,
            Message = "[BEHAV/SLEEP] {HumanId} Sleep grace aktivní — penalizace ×{Penalty:F2} (odmítnutí: {DeclineCount}×).")]
        public static partial void SleepGracePenalty(
            this ILogger logger,
            string HumanId,
            double Penalty,
            int DeclineCount);

        [LoggerMessage(
            EventId = 1112,
            Level = LogLevel.Warning,
            Message = "[SLEEP/EMERGENCY] {HumanId} NeedRest:{NeedRest:F1} Energy:{Energy:F1} — cooldown bypassed")]
        public static partial void SleepEmergencyBypass(
            this ILogger logger,
            string HumanId,
            double NeedRest,
            double Energy);

        /// <summary>
        /// Spánek byl zablokován biologickými potřebami (hlad nebo žízeň).
        /// Postava půjde nejdřív jíst nebo pít.
        /// </summary>
        [LoggerMessage(
            EventId = 1113,
            Level = LogLevel.Debug,
            Message = "[SLEEP/BLOCKED] {HumanId} Hunger:{Hunger:F1} Thirst:{Thirst:F1} — sleep blocked by biology")]
        public static partial void SleepBlockedByBiology(
            this ILogger logger,
            string HumanId,
            double Hunger,
            double Thirst);

        #endregion Behavior/Sleep — spánkový subsystém

        #region Behavior/Interaction — kontext

        /// <summary>Kontext postavy byl změněn — nová lokace, hluk, soukromí.</summary>
        [LoggerMessage(
            EventId = 1200,
            Level = LogLevel.Information,
            Message = "[BEHAV/INTERACT] {HumanId} Kontext změněn: lokace='{Location}', hluk={Noise:F2}, přeplněnost={Crowding:F2}.")]
        public static partial void InteractionContextChanged(
            this ILogger logger,
            string HumanId,
            string Location,
            double Noise,
            double Crowding);

        /// <summary>
        /// Výsledek rozhodnutí o přijetí/odmítnutí interakce.
        /// Loguje vypočtenou pravděpodobnost přijetí a výsledek.
        /// </summary>
        [LoggerMessage(
            EventId = 1201,
            Level = LogLevel.Information,
            Message = "[BEHAV/INTERACT] {HumanId} {From} → {To}: p(přijetí)={P:F2}, výsledek={Result}.")]
        public static partial void InteractionOutcomeDecided(
            this ILogger logger,
            string HumanId,
            string From,
            string To,
            double P,
            string Result);

        [LoggerMessage(EventId = 1203, Level = LogLevel.Information, Message = "[BEHAV/INTERACT] {HumanId} {From} -> {To}: {Intent}, {ReprPotential}, {Contraception}")]
        public static partial void SexualEncounterProposed(
            this ILogger logger,
            string HumanId,
            string From,
            string To,
            string Intent,
            string ReprPotential,
            string Contraception);

        /// <summary>Stress and noise caused misattribution — interaction outcome modified.</summary>
        [LoggerMessage(
            EventId = 1204,
            Level = LogLevel.Debug,
            Message = "[BEHAV/INTERACT] {HumanId} Misattribution penalty applied: {From}->{To}, stress={Stress:F1}, noise={Noise:F2}, penalty={Penalty:F2}")]
        public static partial void MisattributionPenaltyApplied(
            this ILogger logger, string HumanId, string From, string To,
            double Stress, double Noise, double Penalty);

        #endregion Behavior/Interaction — kontext

        #region Memory — epizodická paměť

        /// <summary>
        /// Epizoda zakódována do paměti.
        /// </summary>
        /// <remarks>
        /// Loguje se jako <see cref="LogLevel.Debug"/> — v sandboxu jich mohou být stovky za tick.
        /// </remarks>
        [LoggerMessage(
            EventId = 3000,
            Level = LogLevel.Debug,
            Message = "[MEM/ENCODE] {HumanId} Zakódována epizoda: '{Tag}' (salience={Salience:F2}, emotion={Emotion}).")]
        public static partial void MemoryEncoded(
            this ILogger logger,
            string HumanId,
            string Tag,
            double Salience,
            string Emotion);

        /// <summary>Konsolidace paměti proběhla — počet posílených epizod.</summary>
        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Information,
            Message = "[MEM/CONSOLIDATE] {HumanId} Konsolidace: posíleno {Count} epizod.")]
        public static partial void MemoryConsolidated(
            this ILogger logger,
            string HumanId,
            int Count);

        /// <summary>Episodic memory drifted during recall (reconsolidation).</summary>
        [LoggerMessage(
            EventId = 3002,
            Level = LogLevel.Debug,
            Message = "[MEM/RECONSOL] {HumanId} Episode '{Tag}' drifted: emotion {OldEmotion}->{NewEmotion}, drift={DriftFraction:F3}")]
        public static partial void MemoryReconsolidated(
            this ILogger logger, string HumanId,
            string Tag, string OldEmotion, string NewEmotion, double DriftFraction);

        /// <summary>SemanticMemory belief updated significantly (delta > 0.1).</summary>
        [LoggerMessage(
            EventId = 3003,
            Level = LogLevel.Debug,
            Message = "[MEM/BELIEF] {HumanId} Belief about {Other}: {Kind} {OldStrength:F2}->{NewStrength:F2} (evidence={EvidenceCount})")]
        public static partial void BeliefUpdated(
            this ILogger logger, string HumanId, string Other,
            string Kind, double OldStrength, double NewStrength, int EvidenceCount);

        #endregion Memory — epizodická paměť

        #region Relationships — vztahy

        /// <summary>Relationship event recieved</summary>
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "[REL/EVENT] {HumanId} {EventType}: self={Self}, other={Other}, outcome={Outcome}, detail={Detail}.")]
        public static partial void RelEventReceived(
            this ILogger logger,
            string HumanId,
            string EventType,
            string Self,
            string Other,
            string Outcome,
            string Detail);

        /// <summary>Výsledek relationship mutace po zpracování eventu — změněná pole před/po.</summary>
        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Information,
            Message = "[REL/APPLY] {HumanId} {EventType}: self={Self}, other={Other}, outcome={Outcome}, changes={Changes}.")]
        public static partial void RelEventApplied(
            this ILogger logger,
            string HumanId,
            string EventType,
            string Self,
            string Other,
            string Outcome,
            string Changes);

        /// <summary>Nová relationship hrana vytvořena — první kontakt mezi dvěma postavami.</summary>
        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Information,
            Message = "[REL/EDGE] {HumanId} Nová hrana: {From} → {To}.")]
        public static partial void RelEdgeCreated(
            this ILogger logger,
            string HumanId,
            string From,
            string To);

        /// <summary>Relationship hrana aktualizována po eventu — aktuální hodnoty dimenzí.</summary>
        [LoggerMessage(
            EventId = 2005,
            Level = LogLevel.Debug,
            Message = "[REL/EDGE] {HumanId} Hrana {From}→{To}: Like={Like:F1}, Trust={Trust:F1}, Closeness={Closeness:F1}, Comfort={Comfort:F1}, Respect={Respect:F1}, Familiarity={Familiarity:F1}, IntimateAffinity={IntimateAffinity:F1}, SexualInterest={Sexual:F1}, AestheticAttraction={Aesthetic:F1}, PhysicalAttraction={Physical:F1}")]
        public static partial void RelEdgeUpdated(
            this ILogger logger,
            string HumanId,
            string From,
            string To,
            double Like,
            double Trust,
            double Closeness,
            double Comfort,
            double Respect,
            double Familiarity,
            double IntimateAffinity,
            double Sexual,
            double Aesthetic,
            double Physical);

        [LoggerMessage(
            EventId = 2006,
            Level = LogLevel.Debug,
            Message = "[REL/DECAY] {HumanId} Počet hran {EdgesCount} (dny: {Days:F1}).")]
        public static partial void RelDecayApplied(
            this ILogger logger,
            string HumanId,
            int EdgesCount,
            double Days);

        /// <summary>První dojem vytvořen — inicializace relationship hrany z dojmu.</summary>
        [LoggerMessage(
            EventId = 2007,
            Level = LogLevel.Information,
            Message = "[REL/IMPRESSION] {HumanId} První dojem: {A} → {B} Like={Like:F1}.")]
        public static partial void RelFirstImpression(
            this ILogger logger,
            string HumanId,
            string A,
            string B,
            double Like);

        /// <summary>Sexual encounter outcome resolved — accepted or declined.</summary>
        [LoggerMessage(
            EventId = 2008,
            Level = LogLevel.Information,
            Message = "[REL/SEX] {HumanId} Sexual encounter {From}->{To}: outcome={Outcome}, intimateAffinity={IntimateAffinity:F1}, sexualInterest={SexualInterest:F1}, closeness={Closeness:F1}")]
        public static partial void SexualEncounterOutcome(
            this ILogger logger, string HumanId, string From, string To,
            string Outcome, double IntimateAffinity, double SexualInterest, double Closeness);

        /// <summary>Per-edge dimension detail for decay — which dims changed and by how much.</summary>
        [LoggerMessage(
            EventId = 2009,
            Level = LogLevel.Debug,
            Message = "[REL/DECAY] {HumanId} Edge {From}->{To}: {Changes}")]
        public static partial void RelDecayDimensionDetail(
            this ILogger logger, string HumanId, string From, string To, string Changes);

        /// <summary>A relationship dimension crossed a significant milestone threshold.</summary>
        [LoggerMessage(
            EventId = 2010,
            Level = LogLevel.Information,
            Message = "[REL/MILESTONE] {HumanId} {From}->{To}: {Dimension} crossed {Threshold:F0} ({OldValue:F1}->{NewValue:F1})")]
        public static partial void RelationshipMilestoneReached(
            this ILogger logger, string HumanId, string From, string To,
            string Dimension, double Threshold, double OldValue, double NewValue);

        /// <summary>Third-party observation changed B's edge toward A.</summary>
        [LoggerMessage(
            EventId = 2011,
            Level = LogLevel.Debug,
            Message = "[REL/THIRD] {Observer} observed {Actor} act on {Target}: {Dimension} {OldValue:F1}->{NewValue:F1} (weight={Weight:F2})")]
        public static partial void ThirdPartyReputationChanged(
            this ILogger logger, string Observer, string Actor, string Target,
            string Dimension, double OldValue, double NewValue, double Weight);

        /// <summary>Jealousy distress applied — TransgressionResidue increased.</summary>
        [LoggerMessage(
            EventId = 2012,
            Level = LogLevel.Information,
            Message = "[REL/JEALOUSY] {HumanId} Jealousy distress: {IntimateActor}->{IntimateTarget} observed, transgressionResidue {OldResidue:F2}->{NewResidue:F2}")]
        public static partial void JealousyDistressApplied(
            this ILogger logger, string HumanId, string IntimateActor, string IntimateTarget,
            double OldResidue, double NewResidue);

        [LoggerMessage(
            EventId = 1202,
            Level = LogLevel.Information,
            Message = "[BEHAV/TOUCH] {HumanId} {From} → {To}: level={Level}, p(přijetí)={P:F2}, výsledek={Result}.")]
        public static partial void TouchOutcomeDecided(
            this ILogger logger,
            string HumanId,
            string From,
            string To,
            string Level,
            double P,
            string Result);

        #endregion Relationships — vztahy

        #region DailySchedule — 1400–1499

        /// <summary>A new calendar day's slots have been registered with the scheduler.</summary>
        [LoggerMessage(
            EventId = 1400,
            Level = LogLevel.Debug,
            Message = "[SCHEDULE] {HumanId} Day {DayIndex} registered: {SlotCount} slots")]
        public static partial void ScheduleDayRegistered(
            this ILogger logger,
            string HumanId,
            int SlotCount,
            long DayIndex);

        /// <summary>A scheduled slot has fired and the action window is now active.</summary>
        [LoggerMessage(
            EventId = 1401,
            Level = LogLevel.Debug,
            Message = "[SCHEDULE] {HumanId} Slot triggered: {SlotId} → {Action} @ {Location}")]
        public static partial void ScheduleSlotTriggered(
            this ILogger logger,
            string HumanId,
            string SlotId,
            string Action,
            string Location);

        /// <summary>A slot bias has been applied to a behavior candidate.</summary>
        [LoggerMessage(
            EventId = 1402,
            Level = LogLevel.Debug,
            Message = "[SCHEDULE] {HumanId} Bias +{Bias:F2} on {Action} from slot {SlotId}")]
        public static partial void ScheduleSlotBiasApplied(
            this ILogger logger,
            string HumanId,
            double Bias,
            string Action,
            string SlotId);

        /// <summary>A skippable slot was ignored due to high stress or low energy.</summary>
        [LoggerMessage(
            EventId = 1403,
            Level = LogLevel.Debug,
            Message = "[SCHEDULE] {HumanId} Slot {SlotId} skipped (stress={Stress:F1}, energy={Energy:F1})")]
        public static partial void ScheduleSlotSkipped(
            this ILogger logger,
            string HumanId,
            string SlotId,
            double Stress,
            double Energy);

        /// <summary>Daily schedule seeded from occupation at character creation.</summary>
        [LoggerMessage(
            EventId = 1404,
            Level = LogLevel.Information,
            Message = "[SCHEDULE] {HumanId} Schedule seeded: occupation={Occupation}, slots={SlotCount}")]
        public static partial void ScheduleSeeded(
            this ILogger logger,
            string HumanId,
            string Occupation,
            int SlotCount);

        #endregion DailySchedule — 1400–1499

        #region Goals — 1300–1399

        /// <summary>Goal activated — new persistent goal entered the active list.</summary>
        [LoggerMessage(
            EventId = 1300,
            Level = LogLevel.Information,
            Message = "[GOAL] {HumanId} Goal activated: {Kind} (origin={Origin}, salience={Salience:F2})")]
        public static partial void GoalActivated(
            this ILogger logger,
            string HumanId,
            string Kind,
            string Origin,
            double Salience);

        /// <summary>Goal salience or progress changed meaningfully.</summary>
        [LoggerMessage(
            EventId = 1301,
            Level = LogLevel.Debug,
            Message = "[GOAL] {HumanId} {Kind} salience {OldSalience:F2}->{NewSalience:F2} progress {OldProgress:F2}->{NewProgress:F2}")]
        public static partial void GoalProgressed(
            this ILogger logger,
            string HumanId,
            string Kind,
            double OldSalience,
            double NewSalience,
            double OldProgress,
            double NewProgress);

        /// <summary>Goal resolved — completed, abandoned, faded, or displaced.</summary>
        [LoggerMessage(
            EventId = 1302,
            Level = LogLevel.Information,
            Message = "[GOAL] {HumanId} Goal resolved: {Kind} → {Resolution} (progress={Progress:F2}, frustration={Frustration:F2})")]
        public static partial void GoalResolved(
            this ILogger logger,
            string HumanId,
            string Kind,
            string Resolution,
            double Progress,
            double Frustration);

        /// <summary>Goal bias applied to a behavior candidate.</summary>
        [LoggerMessage(
            EventId = 1303,
            Level = LogLevel.Debug,
            Message = "[GOAL] {HumanId} Bias applied to {Action}: +{Bias:F2} from goal {Kind} (salience={Salience:F2})")]
        public static partial void GoalBiasApplied(
            this ILogger logger,
            string HumanId,
            string Action,
            double Bias,
            string Kind,
            double Salience);

        /// <summary>Goal seeded from personality at character creation.</summary>
        [LoggerMessage(
            EventId = 1304,
            Level = LogLevel.Debug,
            Message = "[GOAL] {HumanId} Seeded {Kind} from personality (salience={Salience:F2})")]
        public static partial void GoalSeededFromPersonality(
            this ILogger logger,
            string HumanId,
            string Kind,
            double Salience);

        #endregion Goals — 1300–1399

        #region Scheduler a infrastruktura

        /// <summary>Scheduler byl spuštěn.</summary>
        [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Scheduler: started")]
        public static partial void SchedulerStarted(this ILogger logger);

        /// <summary>Scheduler provedl tick.</summary>
        [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Scheduler: run tick")]
        public static partial void SchedulerRun(this ILogger logger);

        /// <summary>Scheduler neměl co zpracovat — čeká.</summary>
        [LoggerMessage(EventId = 4102, Level = LogLevel.Debug, Message = "Scheduler: idle")]
        public static partial void SchedulerIdle(this ILogger logger);

        /// <summary>Scheduler selhal — výjimka zachycena.</summary>
        [LoggerMessage(EventId = 4103, Level = LogLevel.Error, Message = "Scheduler: failed")]
        public static partial void SchedulerFailed(this ILogger logger, Exception ex);

        #endregion Scheduler a infrastruktura
    }
}
