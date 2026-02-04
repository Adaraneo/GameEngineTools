// CoreLog.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public static partial class CoreLog
    {
        //#region Behavior

        //[LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
        //    Message = "candidate={Option} u={Utility}")]
        //private static partial void BehaviorCandidateRow(this ILogger logger, string option, float utility);

        //public static void BehaviorCandidate(this ILogger logger, IBehaviorOption opt, float u)
        //    => logger.BehaviorCandidateRow(opt.Type.ToString(), u);

        //[LoggerMessage(EventId = 1000, Level = LogLevel.Debug,
        //    Message = "[Decision] {WhenO} {Actor}->{Target} sit={Situation} stage={Stage} like={Like} trust={Trust} close={Closeness} " +
        //              "normP={NormP} aud={Aud} mood={Mood} stress={Stress} fat={Fatigue} goals.aff={Aff} | TOP={Top} chosen={Chosen}")]
        //private static partial void BehaviorDecisionRow(this ILogger logger,
        //    string whenO, string actor, string target, string situation,
        //    string stage, float like, float trust, float closeness,
        //    float normP, float aud,
        //    float mood, float stress, float fatigue, float aff,
        //    string top, string chosen);

        //public static void BehaviorDecision(this ILogger logger, in BehaviorContext ctx,
        //    IReadOnlyList<(IBehaviorOption opt, float u)> candidates, IBehaviorOption? chosen)
        //{
        //    var top = candidates
        //        .OrderByDescending(c => c.u).Take(5)
        //        .Select(c => $"{c.opt.Type}={c.u:0.000}")
        //        .ToArray();
        //    var chosenStr = chosen is null ? "—" : $"{chosen.Type}";
        //    logger.BehaviorDecisionRow(
        //        ctx.When.ToString("O"),
        //        ctx.Actor.DNA.ToString(), ctx.Target?.DNA.ToString() ?? "—",
        //        ctx.Situation.ToString(),
        //        ctx.Rel?.Stage.ToString() ?? "—",
        //        ctx.Rel?.Liking ?? 0, ctx.Rel?.Trust ?? 0, ctx.Rel?.Closeness ?? 0,
        //        ctx.NormPressure01, ctx.CrowdNoise01,
        //        (float)ctx.Actor.Psychology.Mood, (float)ctx.Actor.Psychology.StressLevel, (float)ctx.Actor.Psychology.Fatigue,
        //        ctx.Goals.AffiliationNeed01,
        //        string.Join("|", top), chosenStr);
        //}

        //[LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        //    Message = "[Enqueue] {When} {From}->{To} | {Type} Ix(P={Privacy},D={Depth},Tone={Tone})")]
        //public static partial void BehaviorEnqueue(this ILogger logger,
        //    string when, string from, string to, string type,
        //    float privacy, float depth, float tone);

        //[LoggerMessage(EventId = 1002, Level = LogLevel.Debug,
        //    Message = "[Encounter] {WhenHM} {A} ↔ {B} | {Situation} | src={Source} tag={Tag} Ix(P={Privacy},D={Depth},Aud={Audience})")]
        //public static partial void BehaviorEncounter(this ILogger logger,
        //    string whenHM, string a, string b, string situation, string source, string tag,
        //    float privacy, float depth, float audience);

        //#endregion

        #region Relationships

        [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
            Message = "[REL/INTERACTION] {When} {From}->{To} {Type} x{Mag} | L:{L0}->{L1} T:{T0}->{T1} A:{A0}->{A1} C:{C0}->{C1} S:{S0}->{S1}")]
        public static partial void RelInteractionApplied(this ILogger logger,
            string when, string from, string to, string type, float mag,
            float L0, float L1, float T0, float T1, float A0, float A1, float C0, float C1, string S0, string S1);

        [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
            Message = "[REL/STAGE] {When} {From}->{To} {Old} => {New}")]
        public static partial void RelStageChanged(this ILogger logger,
            string when, string from, string to, string Old, string New);

        [LoggerMessage(EventId = 2002, Level = LogLevel.Information,
            Message = "[REL/FAMILY] {When} {A}<->{B} {Change}")]
        public static partial void RelFamilyChanged(this ILogger logger,
            string when, string a, string b, string change);

        [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
            Message = "[REL/DECAY] {When} {From}->{To} dt={DtSec}s | L:{L0}->{L1} T:{T0}->{T1} A:{A0}->{A1} C:{C0}->{C1} S:{S0}->{S1}")]
        public static partial void RelDecayApplied(this ILogger logger,
            string when, string from, string to, double DtSec,
            float L0, float L1, float T0, float T1, float A0, float A1, float C0, float C1, string S0, string S1);

        #endregion
        // --- SCHEDULER/LOOPS ---

        [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Scheduler: started")]
        public static partial void SchedulerStarted(this ILogger logger);

        [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Scheduler: run tick")]
        public static partial void SchedulerRun(this ILogger logger);

        [LoggerMessage(EventId = 4102, Level = LogLevel.Debug, Message = "Scheduler: idle")]
        public static partial void SchedulerIdle(this ILogger logger);

        [LoggerMessage(EventId = 4103, Level = LogLevel.Error, Message = "Scheduler: failed")]
        public static partial void SchedulerFailed(this ILogger logger, Exception ex);
    }
}
