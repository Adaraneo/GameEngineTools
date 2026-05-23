// OrchestratedHuman.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System.Collections.Concurrent;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Orchestrates a single character through the simulation pipeline.
    /// Enforces the fixed engine order: Physiology → Psychology → Behavior → Interactions → Relationships → Memory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two-phase tick model:</b>
    /// <list type="bullet">
    ///   <item><b>Phase A (Handle)</b> — scheduled actions and inbox events are delivered against the previous snapshot.</item>
    ///   <item><b>Phase B (Tick)</b> — each engine advances its state; events accumulate and are published after all engines complete.</item>
    ///   <item><b>Phase C (SelfDeliver)</b> — the character reacts to its own Phase B events (memory, relationships, …).</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class OrchestratedHuman : IHuman
    {
        #region Public properties — identity

        /// <inheritdoc/>
        public HumanId Id { get; }

        /// <inheritdoc/>
        public Identity Identity { get; private set; }

        /// <inheritdoc/>
        public SexBiology Biology { get; }

        /// <inheritdoc/>
        public Personality Personality { get; }

        /// <inheritdoc/>
        public PsychologicalProfile PsychologyProfile { get; }

        /// <inheritdoc/>
        public PhysicalAppearance PhysicalAppearance => _projectedAppearance;

        /// <inheritdoc/>
        public GeneticBlueprint GeneticBlueprint => _geneticBlueprint;

        /// <inheritdoc/>
        public AttractionProfile? AttractionProfile { get; }

        #endregion Public properties — identity

        #region Public properties — runtime state

        /// <inheritdoc/>
        public EnginesSnapshot Snapshot { get; private set; }

        /// <inheritdoc/>
        public IReadOnlyList<IDomainEvent> LastOutbox => _lastOutboxAccumulator;

        /// <inheritdoc/>
        public int Age
        {
            get
            {
                var today = WDateTime.Now.Date;
                var birth = Identity.BirthDate;
                var age = today.Year - birth.Year;

                if (today.Month < birth.Month ||
                   (today.Month == birth.Month && today.Day < birth.Day))
                {
                    age--;
                }

                return age;
            }
        }

        /// <inheritdoc/>
        public StadiumType Stadium => StadiumResolver.Resolve(Math.Max(0, Age));

        #endregion Public properties — runtime state

        #region Private fields

        private readonly List<IDomainEvent> _lastOutboxAccumulator = new();

        // Appearance — blueprint is immutable; projected appearance is recomputed each snapshot refresh
        private readonly GeneticBlueprint _geneticBlueprint;

        private PhysicalAppearance _projectedAppearance;

        // Services
        private readonly IEventBus _bus;

        private readonly IScheduler _scheduler;
        private readonly IRandomSource _random;
        private readonly ILogger _log;

        // Engines
        private readonly IPhysiologyEngine _physio;

        private readonly IPsychologyEngine _psych;
        private readonly IBehaviorEngine _behavior;
        private readonly IInteractionEngine _interact;
        private readonly IRelationshipsEngine _relations;
        private readonly IMemoryEngine _memory;
        private readonly ISemanticMemoryEngine _semanticMemory;
        private readonly IGoalEngine _goal;
        private readonly IDailyScheduleEngine _schedule;
        private readonly IObjectInteractionEngine? _objectInteraction;

        // Inbox of externally delivered events (processed at the start of the next tick — Phase A)
        private readonly ConcurrentQueue<IDomainEvent> _inbox = new();

        // Context shared across a single tick; Snapshot inside is always the previous completed state
        private readonly HumanContext _ctx;

        // Optional cadence decoupling for behaviour-level reasoning.
        // When zero, behavior runs every incoming Tick(dt).
        private readonly Hosting.IBehaviorCadencePolicy? _behaviorCadencePolicy;

        private WTimeSpan _behaviorAccumulated;

        #endregion Private fields

        #region Constructor

        /// <summary>
        /// Initialises the orchestrated character with all required services and engines.
        /// </summary>
        /// <param name="id">Unique character identifier.</param>
        /// <param name="identity">Name and birth date.</param>
        /// <param name="biology">Biological sex.</param>
        /// <param name="personality">Personality traits.</param>
        /// <param name="geneticBlueprint">Immutable genetic blueprint — physical appearance is projected from this at runtime.</param>
        /// <param name="attractionProfile">
        /// Personal attraction preferences, or <c>null</c> for legacy characters loaded from
        /// saves created before this field existed.
        /// </param>
        /// <param name="bus">Event bus for cross-character communication.</param>
        /// <param name="scheduler">Scheduler for deferred actions.</param>
        /// <param name="random">Per-character RNG source.</param>
        /// <param name="logger">Per-character logger.</param>
        /// <param name="physio">Physiology engine instance.</param>
        /// <param name="psych">Psychology engine instance.</param>
        /// <param name="behavior">Behavior engine instance.</param>
        /// <param name="interact">Interaction engine instance.</param>
        /// <param name="relations">Relationships engine instance.</param>
        /// <param name="memory">Memory engine instance.</param>
        /// <param name="semanticMemory">Semantic memory engine instance.</param>
        /// <param name="goal">Goal engine instance.</param>
        /// <param name="schedule">Daily schedule engine instance.</param>
        /// <param name="objectInteraction">Optional object interaction engine. Wired between Interactions and Relationships in Phase B.</param>
        /// <param name="initialSnapshot">Initial engine snapshot (provided by the factory).</param>
        public OrchestratedHuman(
            HumanId id,
            Identity identity,
            SexBiology biology,
            Personality personality,
            GeneticBlueprint geneticBlueprint,
            AttractionProfile? attractionProfile,
            // services
            IEventBus bus,
            IScheduler scheduler,
            IRandomSource random,
            ILogger logger,
            // engines
            IPhysiologyEngine physio,
            IPsychologyEngine psych,
            IBehaviorEngine behavior,
            IInteractionEngine interact,
            IRelationshipsEngine relations,
            IMemoryEngine memory,
            ISemanticMemoryEngine semanticMemory,
            IGoalEngine goal,
            IDailyScheduleEngine schedule,
            // initial snapshot (from factory)
            EnginesSnapshot initialSnapshot,
            // optional behavior cadence override
            Hosting.IBehaviorCadencePolicy? behaviorCadencePolicy = null,
            // optional object interaction engine
            IObjectInteractionEngine? objectInteraction = null)
        {
            Id = id;
            Identity = identity;
            Biology = biology;
            Personality = personality;
            PsychologyProfile = PsychologicalProfile.FromPersonality(personality);
            _geneticBlueprint = geneticBlueprint;
            AttractionProfile = attractionProfile;

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
            _semanticMemory = semanticMemory;
            _goal = goal;
            _schedule = schedule;
            _objectInteraction = objectInteraction;

            Snapshot = initialSnapshot;
            _behaviorCadencePolicy = behaviorCadencePolicy;
            _behaviorAccumulated = WTimeSpan.Zero;

            _ctx = new HumanContext
            {
                Id = Id,
                Identity = Identity,
                Biology = Biology,
                Personality = Personality,
                PsychologyProfile = PsychologyProfile,
                AttractionProfile = AttractionProfile,
                EventBus = _bus,
                Scheduler = _scheduler,
                Random = _random,
                Logger = _log,
                Snapshot = Snapshot
            };

            RefreshAppearance();
        }

        #endregion Constructor

        #region Cadence helpers

        private WTimeSpan ConsumeBehaviorDelta(WTimeSpan dt)
        {
            if (dt <= WTimeSpan.Zero)
            {
                return WTimeSpan.Zero;
            }

            var decisionStep = _behaviorCadencePolicy?.GetDecisionStep(this) ?? WTimeSpan.Zero;

            if (decisionStep <= WTimeSpan.Zero)
            {
                return dt;
            }

            _behaviorAccumulated += dt;
            if (_behaviorAccumulated < decisionStep)
            {
                return WTimeSpan.Zero;
            }

            var behaviorDt = _behaviorAccumulated;
            _behaviorAccumulated = WTimeSpan.Zero;
            return behaviorDt;
        }

        #endregion Cadence helpers

        #region IHuman — public API

        /// <inheritdoc/>
        public void ReceiveEvent(IDomainEvent @event)
        {
            _inbox.Enqueue(@event);
        }

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt)
        {
            if (now.Date < _ctx.Identity.BirthDate)
                return;

            _lastOutboxAccumulator.Clear();

            // Phase A: deliver scheduled actions and all queued inbox events
            // against the previous (last completed) snapshot.
            PhaseA_HandleScheduled(now);
            PhaseA_HandleInbox();

            // Phase B: advance engines. Physiology / psychology / memory always progress with world time,
            // while behaviour-level reasoning can optionally run on a coarser cadence.
            // Pipeline order: Physiology → Psychology → Behavior → Interact → Relations → Memory.
            var outbox = new EventCollector();
            var behaviorDt = ConsumeBehaviorDelta(dt);

            // Physiology and psychology must advance first — behavior reads their current state.
            // This enforces the documented pipeline order: Physiology → Psychology → Behavior.
            _physio.Tick(now, dt, _ctx, outbox);
            _psych.Tick(now, dt, _ctx, outbox);

            // Mid-tick snapshot: behavior now reads the current tick's physiological and
            // psychological state rather than the state from the previous tick.
            RefreshSnapshot();

            if (behaviorDt > WTimeSpan.Zero)
            {
                _behavior.Tick(now, behaviorDt, _ctx, outbox);
            }

            _interact.Tick(now, dt, _ctx, outbox);
            _objectInteraction?.Tick(now, dt, _ctx, outbox);
            _relations.Tick(now, dt, _ctx, outbox);
            _memory.Tick(now, dt, _ctx, outbox);
            _semanticMemory.Tick(now, dt, _ctx, outbox);
            _goal.Tick(now, dt, _ctx, outbox);
            _schedule.Tick(now, dt, _ctx, outbox);

            // Final snapshot after all Phase B engines complete.
            RefreshSnapshot();

            // Phase C: self-deliver — character reacts to its own Phase B events
            var toPublish = new EventCollector();
            SelfDeliver(outbox, toPublish);
            // Self-delivery can still mutate engine state (for example interaction outcomes feeding relationships).
            RefreshSnapshot();

            // Publish events produced during Phase B (other characters receive them next tick)
            PublishOutbox(toPublish);

            LogState();
        }

        /// <inheritdoc/>
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
            _semanticMemory.RestoreState(snapshot.SemanticMemory ?? SemanticMemoryState.Empty);
            _goal.RestoreState(snapshot.Goals ?? GoalState.Empty);
            _schedule.RestoreState(snapshot.Schedule ?? DailyScheduleState.Empty);
        }

        /// <inheritdoc/>
        public void FlushInbox()
        {
            if (_inbox.IsEmpty) return;

            var outbox = new EventCollector();
            while (_inbox.TryDequeue(out var ev))
            {
                SafeHandle(ev, outbox);
            }

            // Rebuild snapshot so Snapshot.Relationships reflects the seeded edges.
            RefreshSnapshot();
        }

        #endregion IHuman — public API

        #region Phase A — Handle

        private void PhaseA_HandleScheduled(WDateTime now)
        {
            var due = _scheduler.Due(now);
            if (due is null)
            {
                return;
            }

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
            if (_inbox.IsEmpty)
            {
                return;
            }

            var outbox = new EventCollector();
            while (_inbox.TryDequeue(out var ev))
            {
                SafeHandle(ev, outbox);
            }

            Deliver(outbox);
        }

        #endregion Phase A — Handle

        #region Phase C — SelfDeliver

        private void SelfDeliver(IEventCollector collector, IEventCollector toPublish)
        {
            const int maxPasses = 8;
            var pass = 0;
            var localOutbox = new EventCollector();

            while (pass++ < maxPasses)
            {
                var events = collector.Drain();
                if (events.Count == 0)
                {
                    break;
                }

                foreach (var ev in events)
                {
                    toPublish.Add(ev);
                    SafeHandle(ev, localOutbox);
                }

                var secondary = localOutbox.Drain();
                foreach (var ev in secondary)
                {
                    collector.Add(ev);
                }
            }
        }

        #endregion Phase C — SelfDeliver

        #region Private helpers

        private void SafeHandle(IDomainEvent ev, IEventCollector outbox)
        {
            try { _physio.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Physiology.Handle failed.", Id.Value); }
            try { _psych.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Psychology.Handle failed.", Id.Value); }
            try { _behavior.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Behavior.Handle failed.", Id.Value); }
            try { _interact.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Interactions.Handle failed.", Id.Value); }
            try { _relations.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Relationships.Handle failed.", Id.Value); }
            try { _memory.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Memory.Handle failed.", Id.Value); }
            try { _semanticMemory.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] SemanticMemory.Handle failed.", Id.Value); }
            if (_objectInteraction is not null) try { _objectInteraction.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] ObjectInteraction.Handle failed.", Id.Value); }
            try { _goal.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Goal.Handle failed.", Id.Value); }
            try { _schedule.Handle(ev, _ctx, outbox); } catch (Exception ex) { _log.LogError(ex, "[{Human}] Schedule.Handle failed.", Id.Value); }
        }

        private void Deliver(IEventCollector collector)
        {
            const int maxPasses = 8;
            var pass = 0;
            var toPublish = new EventCollector();

            while (pass++ < maxPasses)
            {
                var events = collector.Drain();
                if (events.Count == 0)
                {
                    break;
                }

                foreach (var ev in events)
                {
                    toPublish.Add(ev);
                    SafeHandle(ev, collector);
                }
            }

            if (pass > maxPasses && collector.Drain().Count > 0)
            {
                _log.LogWarning("[{Human}] Deliver: maxPasses={Max} reached, events discarded!", Id.Value, maxPasses);
            }

            PublishOutbox(toPublish);
        }

        private void PublishOutbox(IEventCollector collector)
        {
            var events = collector.Drain();
            _lastOutboxAccumulator.AddRange(events);

            foreach (var ev in events)
            {
                try
                {
                    _bus.Publish(ev);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "[{Human}] EventBus.Publish failed for {EventType}.", Id.Value, ev.GetType().Name);
                }
            }
        }

        /// <summary>
        /// Rebuilds the externally visible snapshot from the current live engine states.
        /// Called after both Phase B and Phase C so consumers see the final state of the tick.
        /// </summary>
        private void RefreshAppearance()
            => _projectedAppearance = Generation.AppearanceProjector.Project(_geneticBlueprint, Age);

        private void RefreshSnapshot()
        {
            RefreshAppearance();
            var prev = Snapshot;
            Snapshot = new EnginesSnapshot(
                _physio.State,
                _psych.State,
                _behavior.State,
                _interact.State,
                _relations.State,
                _memory.State,
                _semanticMemory.State,
                AmbientTemperature: prev.AmbientTemperature,
                AltitudeMeters: prev.AltitudeMeters,
                Celestial: prev.Celestial,
                Goals: _goal.State,
                Schedule: _schedule.State);

            _ctx.Snapshot = Snapshot;
        }

        /// <inheritdoc/>
        public void SetAmbientContext(double ambientTempC, CelestialContext? celestial)
        {
            Snapshot = Snapshot with
            {
                AmbientTemperature = ambientTempC,
                Celestial = celestial,
            };
            _ctx.Snapshot = Snapshot;
        }

        /// <inheritdoc/>
        public void SetHomeLocation(string? locationId)
        {
            Identity = Identity with { HomeLocationId = locationId };
            _ctx.Identity = Identity;
        }

        /// <inheritdoc/>
        public void ChangeOccupation(string? newOccupationId)
        {
            _schedule.SeedFromOccupation(
                newOccupationId,
                Personality,
                WDateTime.Now,
                _scheduler,
                Id);
        }

        public void SetLastName(IHuman partner)
        {
            if (_ctx.Snapshot.Relationships.Edges is not null)
            {
                _ctx.Snapshot.Relationships.Deconstruct(out var edges);
                if (edges.Count == 0) return;
                var partnerId = edges.First(edge => edge.Value.KinRole == KinRole.Partner && edge.Key == partner.Id);
                if (partnerId.Key != partner.Id) return;
                Identity = Identity with { LastName = partner.Identity.LastName };
                _ctx.Identity = Identity;
            }
        }

        private void LogState()
        {
            var s = Snapshot;

            // Snapshots must be logged inside a character scope so that PersonId
            // is set on the log entry. Without it the log reader cannot attribute
            // these events to the correct character (they land in Characters.jsonl
            // with PersonId=null and are filtered out by the reader).
            using (_log.BeginCharacterScope(Id.Value, nameof(OrchestratedHuman)))
            {
                _log.PhysiologySnapshot(
                    Id.Value.ToString(),
                    s.Physiology.Energy, s.Physiology.Hunger, s.Physiology.Thirst,
                    s.Physiology.Pain, s.Physiology.SleepDebtHours,
                    s.Physiology.BodyTempDelta, s.Physiology.ImmuneLoad);

                if (s.Physiology.Cycle is { } c)
                {
                    _log.PhysiologyCycle(Id.Value.ToString(), c.Phase.ToString(), c.DayInCycle);
                }

                _log.PsychologySnapshot(
                    Id.Value.ToString(),
                    s.Psychology.DominantEmotion.ToString(),
                    s.Psychology.Valence, s.Psychology.Arousal, s.Psychology.Dominance,
                    s.Psychology.Stress, s.Psychology.CognitiveLoad);

                var plan = s.Behavior.CurrentPlan;
                _log.BehaviorSnapshot(
                    Id.Value.ToString(),
                    plan?.Name ?? "—",
                    s.Behavior.NeedRest, s.Behavior.NeedFood, s.Behavior.NeedWater,
                    s.Behavior.NeedBelonging, s.Behavior.NeedCompetence, s.Behavior.NeedIntimacy);

                if (plan is not null)
                {
                    _log.BehaviorPlan(
                        Id.Value.ToString(),
                        plan.Name, plan.Start.ToString(), plan.ExpectedDuration.ToString(), plan.Utility);
                }
            }
        }

        #endregion Private helpers

        #region Object overrides

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is IHuman other && Id == other.Id;

        /// <inheritdoc/>
        public override int GetHashCode()
            => Id.GetHashCode();

        public override string ToString()
        {
            var fname = Identity.FirstName.Original;
            if (this.Biology == SexBiology.Female)
            {
                return string.Join(" ", fname, this.Identity.LastName.Female);
            }

            return string.Join(" ", fname, this.Identity.LastName.Male);
        }

        #endregion Object overrides
    }
}
