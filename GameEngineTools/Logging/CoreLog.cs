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
        public static partial void PsychSleepInterrupted(this ILogger logger, string HumanId, double Quality, double StressDelta);

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

        #endregion Memory — epizodická paměť

        #region Relationships — vztahy

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
            Message = "[REL/EDGE] {HumanId} Hrana {From}→{To}: Like={Like:F1}, Trust={Trust:F1}, Closeness={Closeness:F1}, Attraction={Attraction:F1}, Comfort={Comfort:F1}, Respect={Respect:F1}.")]
        public static partial void RelEdgeUpdated(
            this ILogger logger,
            string HumanId,
            string From,
            string To,
            double Like,
            double Trust,
            double Closeness,
            double Attraction,
            double Comfort,
            double Respect);

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
            Message = "[REL/IMPRESSION] {HumanId} První dojem: {A} → {B} Like={Like:F1} Attraction={Attraction:F1}.")]
        public static partial void RelFirstImpression(
            this ILogger logger,
            string HumanId,
            string A,
            string B,
            double Like,
            double Attraction);

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
