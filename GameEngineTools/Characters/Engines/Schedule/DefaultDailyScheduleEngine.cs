// DefaultDailyScheduleEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class DefaultDailyScheduleEngine : IDailyScheduleEngine
    {
        #region State and configuration

        /// <inheritdoc/>
        public DailyScheduleState State { get; private set; }

        /// <inheritdoc/>
        public DailyScheduleConfig Config { get; }

        #endregion

        #region Construction

        private readonly ILogger _log;

        public DefaultDailyScheduleEngine(IOptions<DailyScheduleConfig> cfg, ILogger<DefaultDailyScheduleEngine> log)
        {
            Config = cfg.Value;
            _log = log;
            State = DailyScheduleState.Empty;
        }

        #endregion

        #region IDailyScheduleEngine — SeedFromOccupation

        /// <inheritdoc/>
        public void SeedFromOccupation(
            OccupationKind occupation,
            Personality personality,
            WDateTime now,
            IScheduler scheduler,
            HumanId humanId)
        {
            var slots = OccupationScheduleSeeder.Seed(occupation, personality);
            State = new DailyScheduleState(slots, null, -1, occupation);

            // Register today's slots immediately
            ScheduleDay(now.Date.ToDateTime(), scheduler, humanId);

            // Log seeding
            using (_log.BeginCharacterScope(humanId.Value, nameof(DefaultDailyScheduleEngine)))
            {
                _log.ScheduleSeeded(humanId.Value.ToString(), occupation.ToString(), slots.Count);
            }
        }

        #endregion

        #region IEngine — Tick

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Clear active slot from previous tick
            var newState = State.ActiveSlot is not null
                ? State with { ActiveSlot = null }
                : State;

            // Check whether we need to pre-schedule a future day
            // Look ahead by RescheduleLeadHours to catch the next calendar day
            var leadAhead  = now + WTimeSpan.FromHours(Config.RescheduleLeadHours);
            var leadDayIdx = leadAhead.Date.DayIndex;

            if (leadDayIdx > newState.LastScheduledDayIndex && newState.Slots.Count > 0)
            {
                ScheduleDay(leadAhead.Date.ToDateTime(), ctx.Scheduler, ctx.Id, outbox, now);
                newState = newState with { LastScheduledDayIndex = leadDayIdx };
            }

            State = newState;
        }

        #endregion

        #region IEngine — Handle

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is not ScheduleSlotTriggered sst || sst.Human != ctx.Id)
                return;

            var slot = State.Slots.FirstOrDefault(s => s.SlotId == sst.SlotId);
            if (slot is null) return;

            State = State with { ActiveSlot = slot };

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultDailyScheduleEngine)))
            {
                _log.ScheduleSlotTriggered(
                    ctx.Id.Value.ToString(),
                    sst.SlotId,
                    sst.PreferredAction,
                    sst.PreferredLocationId ?? "—");
            }
        }

        #endregion

        #region IEngine — RestoreState

        /// <inheritdoc/>
        public void RestoreState(DailyScheduleState state)
        {
            // Reset LastScheduledDayIndex so the first Tick() after restore re-registers
            // today's slots with the (now empty) scheduler. Without this, the scheduler
            // would have no callbacks and no slots would ever fire after a save/load.
            State = state with { LastScheduledDayIndex = -1, ActiveSlot = null };
        }

        #endregion

        #region Private helpers

        private void ScheduleDay(
            WDateTime dayStart,
            IScheduler scheduler,
            HumanId humanId,
            IEventCollector? outbox = null,
            WDateTime? occurredAt = null)
        {
            if (State.Slots.Count == 0) return;

            var dayIndex = dayStart.Date.DayIndex;

            foreach (var slot in State.Slots)
            {
                var fireAt = dayStart.AddHours(slot.HourOfDay);
                var capturedSlot = slot;  // capture for closure

                scheduler.ScheduleAt(fireAt, (sCtx, sOutbox) =>
                {
                    sOutbox.Add(new ScheduleSlotTriggered(
                        fireAt,
                        sCtx.Id,
                        capturedSlot.SlotId,
                        capturedSlot.PreferredAction,
                        capturedSlot.PreferredLocationId,
                        capturedSlot.BiasStrength));
                }, tag: $"schedule:{slot.SlotId}");
            }

            State = State with { LastScheduledDayIndex = dayIndex };

            var at = occurredAt ?? dayStart;
            outbox?.Add(new ScheduleDayRegistered(at, humanId, State.Slots.Count, dayIndex));

            using (_log.BeginCharacterScope(humanId.Value, nameof(DefaultDailyScheduleEngine)))
            {
                _log.ScheduleDayRegistered(humanId.Value.ToString(), State.Slots.Count, dayIndex);
            }
        }

        #endregion
    }
}
