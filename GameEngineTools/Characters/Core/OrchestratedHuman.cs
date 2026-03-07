// OrchestratedHuman.cs
// Copyright (c) 50PSoftware

using System.Collections.Concurrent;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Traits;
using GameEngineTools.Logging;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Logging;

namespace GameEngineTools.Characters.Core
{

    /// <summary>
    /// Orchestrátor jedné postavy. Dodržuje pevné pořadí enginů:
    /// Physiology → Psychology → Behavior → Interactions → Relationships → Memory.
    /// Události přijaté zvnějšku a splatné plánované akce se zpracovávají ve fázi A (Handle),
    /// během fáze B (Tick) se události pouze hromadí a publikují se až po dokončení.
    /// </summary>
    public sealed class OrchestratedHuman : IHuman
    {
        public HumanId Id { get; }
        public Identity Identity { get; }
        public SexBiology Biology { get; }
        public Personality Personality { get; }

        public EnginesSnapshot Snapshot { get; private set; }

        // Služby
        private readonly IEventBus _bus;
        private readonly IScheduler _scheduler;
        private readonly IRandomSource _random;
        private readonly ILogger _log;

        // Enginy
        private readonly IPhysiologyEngine _physio;
        private readonly IPsychologyEngine _psych;
        private readonly IBehaviorEngine _behavior;
        private readonly IInteractionEngine _interact;
        private readonly IRelationshipsEngine _relations;
        private readonly IMemoryEngine _memory;

        // Inbox událostí zvenku (doručí se až ve fázi A dalšího ticku)
        private readonly ConcurrentQueue<IDomainEvent> _inbox = new();

        // Kontext sdílený napříč tickem; Snapshot v něm je vždy „minulý“
        private readonly HumanContext _ctx;

        public OrchestratedHuman(
            HumanId id,
            Identity identity,
            SexBiology biology,
            Traits.Personality personality,
            // služby
            IEventBus bus,
            IScheduler scheduler,
            IRandomSource random,
            ILogger logger,
            // enginy
            IPhysiologyEngine physio,
            IPsychologyEngine psych,
            IBehaviorEngine behavior,
            IInteractionEngine interact,
            IRelationshipsEngine relations,
            IMemoryEngine memory,
            // počáteční snapshot (např. z factory)
            EnginesSnapshot initialSnapshot)
        {
            Id = id;
            Identity = identity;
            Biology = biology;
            Personality = personality;

            _bus = bus;
            _scheduler = scheduler;
            _random = random;
            _log = logger;

            _physio = physio;
            _psych = psych;
            _behavior = behavior;
            _interact = interact;
            _relations = relations;
            _memory = memory;

            Snapshot = initialSnapshot;

            _ctx = new HumanContext
            {
                Id = Id,
                Identity = Identity,
                Biology = Biology,
                Personality = Personality,
                EventBus = _bus,
                Scheduler = _scheduler,
                Random = _random,
                Logger = _log,
                Snapshot = Snapshot
            };
        }

        public void ReceiveEvent(IDomainEvent @event)
        {
            _inbox.Enqueue(@event);
        }

        public void Tick(WDateTime now, WTimeSpan dt)
        {
            // FÁZE A: nejdřív splatné plánované akce a všechny frontované události
            // — doručíme je do Handle() enginů na základě „minulého“ snapshotu.
            PhaseA_HandleScheduled(now);
            PhaseA_HandleInbox();

            // FÁZE B: výpočet nových stavů enginů v pevně daném pořadí.
            // Události dočasně ukládáme do outboxu; publikujeme/předáme až po dokončení fáze.
            var outbox = new EventCollector();

            _behavior.Tick(now, dt, _ctx, outbox);
            _physio.Tick(now, dt, _ctx, outbox);
            _psych.Tick(now, dt, _ctx, outbox);
            _interact.Tick(now, dt, _ctx, outbox);
            _relations.Tick(now, dt, _ctx, outbox);
            _memory.Tick(now, dt, _ctx, outbox);

            // After-tick: sestav nový snapshot z aktuálních stavů enginů (double-buffering)
            var newSnapshot = new EnginesSnapshot(
                _physio.State,
                _psych.State,
                _behavior.State,
                _interact.State,
                _relations.State,
                _memory.State);

            Snapshot = newSnapshot;
            _ctx.Snapshot = Snapshot; // kontext dál vždy nese poslední dokončený stav

            // *** FÁZE C: vlastní eventy doručíme sami sobě ***
            // Postava reaguje na to, co sama udělala (paměť, vztahy, atd.)
            SelfDeliver(outbox);

            // Publikace událostí vzniklých během fáze B (dorazí ostatním až v dalším ticku)
            PublishOutbox(outbox);

            LogState();
        }

        private void PhaseA_HandleScheduled(WDateTime now)
        {
            var due = _scheduler.Due(now);
            if (due is null) return;

            var outbox = new EventCollector();
            foreach (var (_, action) in due)
            {
                try
                {
                    action(_ctx, outbox);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "[{Human}] ScheduledAction threw.", Id.Value);
                }
            }
            Deliver(outbox);
        }

        private void PhaseA_HandleInbox()
        {
            if (_inbox.IsEmpty) return;

            var outbox = new EventCollector();
            while (_inbox.TryDequeue(out var ev))
            {
                SafeHandle(ev, outbox);
            }
            Deliver(outbox);
        }

        private void SafeHandle(IDomainEvent ev, IEventCollector outbox)
        {
            // Handle() každého engine; výjimky nepropadnou ven (logujeme, pokračujeme)
            try { _physio.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Physiology.Handle failed.", Id.Value); }
            try { _psych.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Psychology.Handle failed.", Id.Value); }
            try { _behavior.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Behavior.Handle failed.", Id.Value); }
            try { _interact.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Interactions.Handle failed.", Id.Value); }
            try { _relations.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Relationships.Handle failed.", Id.Value); }
            try { _memory.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Memory.Handle failed.", Id.Value); }
        }

        private void Deliver(IEventCollector collector)
        {
            const int maxPasses = 8;
            int pass = 0;

            while (pass++ < maxPasses)
            {
                var events = collector.Drain();
                if (events.Count == 0)
                    break;

                foreach (var ev in events)
                    SafeHandle(ev, collector);
            }

            PublishOutbox(collector);
        }

        private void PublishOutbox(IEventCollector collector)
        {
            var events = collector.Drain();
            foreach (var ev in events)
            {
                try {
                    _bus.Publish(ev);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "[{Human}] EventBus.Publish failed for {EventType}.", Id.Value, ev.GetType().Name);
                }
            }
        }

        public void RestoreSnapshot(EnginesSnapshot snapshot)
        {
            Snapshot = snapshot;
            _ctx.Snapshot = snapshot;
            _physio.RestoreState(snapshot.Physiology);
            _psych.RestoreState(snapshot.Psychology);
            _behavior.RestoreState(snapshot.Behavior);
            _interact.RestoreState(snapshot.InteractionSurface);
            _relations.RestoreState(snapshot.Relationships);
            _memory.RestoreState(snapshot.Memory);
        }

        private void SelfDeliver(IEventCollector collector)
        {
            const int maxPasses = 8;
            int pass = 0;

            var localOutbox = new EventCollector();

            while (pass++ < maxPasses)
            {
                var events = collector.Drain();
                if (events.Count == 0)
                    break;

                foreach (var ev in events)
                    SafeHandle(ev, localOutbox);

                var secondary = localOutbox.Drain();
                foreach (var ev in secondary)
                    collector.Add(ev);
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is IHuman other && Id == other.Id;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        private void LogState()
        {
            var s = Snapshot;
            _log.PhysiologySnapshot(Id.Value.ToString(),
                s.Physiology.Energy, s.Physiology.Hunger, s.Physiology.Thirst,
                s.Physiology.Pain, s.Physiology.SleepDebtHours,
                s.Physiology.BodyTempDelta, s.Physiology.ImmuneLoad);

            if (s.Physiology.Cycle is { } c)
                _log.PhysiologyCycle(Id.Value.ToString(), c.Phase.ToString(), c.DayInCycle);

            _log.PsychologySnapshot(Id.Value.ToString(),
                s.Psychology.DominantEmotion.ToString(),
                s.Psychology.Valence, s.Psychology.Arousal, s.Psychology.Dominance,
                s.Psychology.Stress, s.Psychology.CognitiveLoad);

            var plan = s.Behavior.CurrentPlan;
            _log.BehaviorSnapshot(Id.Value.ToString(),
                plan?.Name ?? "—",
                s.Behavior.NeedRest, s.Behavior.NeedFood, s.Behavior.NeedWater,
                s.Behavior.NeedBelonging, s.Behavior.NeedCompetence, s.Behavior.NeedIntimacy);

            if (plan is not null)
                _log.BehaviorPlan(Id.Value.ToString(),
                    plan.Name, plan.Start.ToString(), plan.ExpectedDuration.ToString(), plan.Utility);
        }
    }
}
