// LogIds.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    using Microsoft.Extensions.Logging;

    public static class LogIds
    {
        public static readonly EventId BehaviorCandidate = new(1001, nameof(BehaviorCandidate));

        public static readonly EventId BehaviorDecision = new(1000, nameof(BehaviorDecision));

        public static readonly EventId BehaviorEncounter = new(1002, nameof(BehaviorEncounter));
        public static readonly EventId BehaviorEnqueue = new(1003, nameof(BehaviorEnqueue));

        public static readonly EventId BusPublish = new(3000, nameof(BusPublish));

        public static readonly EventId BusSubscribe = new(3001, nameof(BusSubscribe));

        public static readonly EventId DailySvcStarted = new(4000, nameof(DailySvcStarted));

        public static readonly EventId DailyTickDone = new(4002, nameof(DailyTickDone));
        public static readonly EventId DailyTickFailed = new(4003, nameof(DailyTickFailed));
        public static readonly EventId DailyTickStart = new(4001, nameof(DailyTickStart));
        public static readonly EventId RelDecayApplied = new(2003, nameof(RelDecayApplied));

        public static readonly EventId RelFamilyChanged = new(2002, nameof(RelFamilyChanged));

        public static readonly EventId RelInteractionApplied = new(2000, nameof(RelInteractionApplied));

        public static readonly EventId RelStageChanged = new(2001, nameof(RelStageChanged));
        public static readonly EventId SchedulerFailed = new(4103, nameof(SchedulerFailed));

        public static readonly EventId SchedulerIdle = new(4102, nameof(SchedulerIdle));

        public static readonly EventId SchedulerRun = new(4101, nameof(SchedulerRun));

        public static readonly EventId SchedulerStarted = new(4100, nameof(SchedulerStarted));
        public static readonly EventId RelDecayStarted = new(4200, nameof(RelDecayStarted));
        public static readonly EventId RelDecayTick = new(4201, nameof(RelDecayTick));
        public static readonly EventId RelDecayFailed = new(4202, nameof(RelDecayFailed));
    }
}
