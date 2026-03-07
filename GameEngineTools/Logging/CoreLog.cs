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
        #region Physiology
        [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "[PHYSIO] {HumanId} Energy: {Energy:F1} Hunger: {Hunger:F2} Thirst: {Thirst:F2} Pain:{Pain:F1} SleepDebt:{SleepDebt:F1}h Temp:{TempDelta:+0.0;-0.0}°C Immune:{Immune:F1}")]
        public static partial void PhysiologySnapshot(this ILogger logger, string HumanId, double Energy, double Hunger, double Thirst, double Pain, double SleepDebt, double TempDelta, double Immune);

        [LoggerMessage(EventId = 5001, Level = LogLevel.Debug,
    Message = "[PHYSIO/CYCLE] {HumanId} Phase:{Phase} Day:{DayInCycle}")]
        public static partial void PhysiologyCycle(this ILogger logger, string HumanId, string Phase, int DayInCycle);
        #endregion

        #region Psychology

        [LoggerMessage(EventId = 5100, Level = LogLevel.Information,
            Message = "[PSYCH] {HumanId} Emotion:{Emotion} V:{Valence:+0.00;-0.00} A:{Arousal:F2} D:{Dominance:F2} Stress:{Stress:F1} CogLoad:{CogLoad:F1}")]
        public static partial void PsychologySnapshot(this ILogger logger,
            string HumanId, string Emotion, double Valence, double Arousal,
            double Dominance, double Stress, double CogLoad);

        #endregion

        #region Behavior

        [LoggerMessage(EventId = 5200, Level = LogLevel.Information,
            Message = "[BEHAV] {HumanId} Plan:{Plan} Rest:{Rest:F1} Food:{Food:F1} Water:{Water:F1} Belonging:{Belonging:F1} Competence:{Competence:F1} Intimacy:{Intimacy:F1}")]
        public static partial void BehaviorSnapshot(this ILogger logger,
            string HumanId, string Plan, double Rest, double Food, double Water,
            double Belonging, double Competence, double Intimacy);

        [LoggerMessage(EventId = 5201, Level = LogLevel.Debug,
            Message = "[BEHAV/PLAN] {HumanId} Action:{Action} Start:{Start} Duration:{Duration} Utility:{Utility:F3}")]
        public static partial void BehaviorPlan(this ILogger logger,
            string HumanId, string Action, string Start, string Duration, double Utility);

        #endregion

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
