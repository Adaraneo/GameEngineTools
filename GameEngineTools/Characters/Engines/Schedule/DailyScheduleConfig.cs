// DailyScheduleConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    /// <summary>
    /// Tuning parameters for the daily schedule engine.
    /// Binds from <c>Characters:DailySchedule</c> in appsettings.
    /// </summary>
    public sealed record DailyScheduleConfig(
        /// <summary>
        /// Maximum flat utility bias added to a candidate by a single active slot.
        /// Default 14.0 — strong enough to compete with GoalBias (max 12) but not dominant.
        /// </summary>
        double MaxSlotFlatBias = 14.0,

        /// <summary>
        /// Flat bias added to a <c>MoveTo</c> candidate when the character is not
        /// at the slot's preferred location. Default 8.0.
        /// </summary>
        double MoveToLocationBias = 8.0,

        /// <summary>
        /// Stress level above which <see cref="ScheduleSlot.CanSkipWhenStressed"/> slots
        /// are ignored. Default 70.0.
        /// </summary>
        double SkipStressThreshold = 70.0,

        /// <summary>
        /// Energy level below which <see cref="ScheduleSlot.CanSkipWhenStressed"/> slots
        /// are ignored. Default 25.0.
        /// </summary>
        double SkipEnergyThreshold = 25.0,

        /// <summary>
        /// How many hours ahead of midnight to schedule tomorrow's slots. Default 1.0.
        /// When <c>now + RescheduleLeadHours</c> crosses into a new calendar day that
        /// has not been registered yet, that day's slots are registered immediately.
        /// </summary>
        double RescheduleLeadHours = 1.0
    )
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public DailyScheduleConfig() : this(14.0, 8.0, 70.0, 25.0, 1.0) { }
    }
}
