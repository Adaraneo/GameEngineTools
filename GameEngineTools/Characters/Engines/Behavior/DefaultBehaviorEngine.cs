// DefaultBehaviorEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using Arbitration;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior.Intent;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Behavior.Sleep;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static ActionNames;

    /// <summary>
    /// Public behavior engine entry point that orchestrates the internal multi-engine behavior pipeline.
    /// </summary>
    internal sealed class DefaultBehaviorEngine : IBehaviorEngine
    {
        #region Private fields

        private readonly ILogger _log;
        private readonly IReadOnlyList<IBehaviorNeedEngine> _needEngines;
        private readonly IReadOnlyList<IBehaviorModifierEngine> _modifierEngines;
        private readonly ISleepCoordinator _sleepCoordinator;
        private readonly IIntentManagementEngine _intentManagementEngine;
        private readonly IActionArbitrationEngine _arbitrationEngine;
        private readonly ICharacterDevelopmentPolicy _developmentPolicy;
        private readonly IHabitApplicabilityModulator _habitApplicabilityModulator;

        /// <summary>
        /// Optional provider of world objects at the character's current location.
        /// Populated at construction time; <c>null</c> means no object-affordance modulation.
        /// </summary>
        private readonly IMutableWorldObjectProvider? _mutableObjectProvider;

        /// <summary>
        /// Concrete CSV provider reference used by <see cref="Modifiers.ObjectInteractionBehaviorModifier"/>
        /// for Drop candidate generation (finding held objects). <c>null</c> when no CSV provider is wired.
        /// </summary>
        private readonly CsvWorldObjectProvider? _csvObjectProvider;

        /// <summary>
        /// Constraint gate that runs after all modifier engines and removes or suppresses
        /// candidates whose required world objects are absent from the current location.
        /// Always active — does not depend on any optional provider.
        /// </summary>
        private readonly ObjectAffordanceGatingEngine _objectAffordanceGatingEngine;

        #endregion Private fields

        #region Public properties

        public BehaviorState State { get; private set; }

        public BehaviorConfig Config { get; }

        #endregion Public properties

        #region Construction

        public DefaultBehaviorEngine(
            IOptions<BehaviorConfig> cfg,
            IOptions<SleepConfig> sleepCfg,
            ILoggerFactory loggerFactory,
            ICharacterDevelopmentPolicy? developmentPolicy = null,
            IHabitApplicabilityModulator? habitApplicabilityModulator = null,
            /// <summary>
            /// Optional world object provider.
            /// When supplied, <see cref="WorldObjectAffordanceEngine"/> is added to the
            /// modifier pipeline and nudges candidate utility based on objects in the
            /// character's current location.
            /// </summary>
            IMutableWorldObjectProvider? objectProvider = null,
            IOptions<DailyScheduleConfig>? scheduleCfg = null,
            CsvWorldObjectProvider? csvObjectProvider = null)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultBehaviorEngine>();
            State = new BehaviorState(40, 30, 25, 50, 50, 35, null, new Dictionary<string, double>());
            _needEngines = new IBehaviorNeedEngine[] { new PhysiologicalNeedsEngine(), new SocialNeedsEngine(), new CompetenceNeedsEngine(), new AutonomyExplorationNeedsEngine(), new ContingencySearchEngine() };
            _csvObjectProvider = csvObjectProvider;
            _modifierEngines = new IBehaviorModifierEngine[] { new TraitBiasEngine(), new PsychologicalConflictBiasEngine(), new AffectiveStateEngine(), new CircadianArousalEngine(), new HabitRoutineEngine(), new LearnedHabitEngine(loggerFactory.CreateLogger<LearnedHabitEngine>()), new MemoryInfluenceEngine(), new EnvironmentalAffordanceEngine(), new WorldObjectAffordanceEngine(), new ObjectInteractionBehaviorModifier(_csvObjectProvider), new GoalBehaviorModifier(loggerFactory.CreateLogger<GoalBehaviorModifier>()), new DailyScheduleBehaviorModifier(loggerFactory.CreateLogger<DailyScheduleBehaviorModifier>(), scheduleCfg?.Value) };
            _objectAffordanceGatingEngine = new ObjectAffordanceGatingEngine();
            _sleepCoordinator = new DefaultSleepCoordinator(sleepCfg.Value, Config, loggerFactory);
            _intentManagementEngine = new DefaultIntentManagementEngine(loggerFactory.CreateLogger<DefaultIntentManagementEngine>());
            _arbitrationEngine = new DefaultActionArbitrationEngine(loggerFactory.CreateLogger<DefaultActionArbitrationEngine>());
            _developmentPolicy = developmentPolicy ?? new DefaultCharacterDevelopmentPolicy();
            _habitApplicabilityModulator = habitApplicabilityModulator ?? NoOpHabitApplicabilityModulator.Instance;
            _mutableObjectProvider = objectProvider;
        }

        #endregion Construction

        #region IEngine

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Build the tick-local behavior context before delegating to sub-engines.
            var updatedCooldowns = BehaviorMath.UpdateCooldowns(State.Cooldowns, Math.Max(0, dt.TotalHours));
            var stateWithHabits = State with { HabitTraces = BehaviorHabitLearning.Decay(State.HabitTraces, dt, Config, ctx, _log) };
            var previousNeeds = stateWithHabits;
            var stateWithNeeds = BehaviorMath.ComputeNeedState(ctx, updatedCooldowns, stateWithHabits) with { Cooldowns = updatedCooldowns };

            // Log need threshold crossings (thresholds: 70 and 85)
            {
                var needThresholds = new[] { 70.0, 85.0 };
                void CheckNeed(string name, double before, double after)
                {
                    foreach (var t in needThresholds)
                    {
                        if (before < t && after >= t)
                        {
                            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultBehaviorEngine)))
                            {
                                _log.NeedThresholdCrossed(ctx.Id.Value.ToString(), name, t, before, after);
                            }
                        }
                    }
                }
                CheckNeed("Rest", previousNeeds.NeedRest, stateWithNeeds.NeedRest);
                CheckNeed("Food", previousNeeds.NeedFood, stateWithNeeds.NeedFood);
                CheckNeed("Water", previousNeeds.NeedWater, stateWithNeeds.NeedWater);
                CheckNeed("Belonging", previousNeeds.NeedBelonging, stateWithNeeds.NeedBelonging);
                CheckNeed("Intimacy", previousNeeds.NeedIntimacy, stateWithNeeds.NeedIntimacy);
            }

            State = stateWithNeeds;

            IReadOnlyList<WorldObject>? availableObjects = null;
            if (_mutableObjectProvider is not null)
            {
                var locationId = ctx.Snapshot.InteractionSurface.Location;
                if (!string.IsNullOrEmpty(locationId))
                {
                    availableObjects = _mutableObjectProvider
                        .GetObjectsAt(locationId)
                        .Where(o => o.IsAvailable)
                        .ToList();
                }
                else
                {
                    availableObjects = new List<WorldObject>();
                }
            }

            var context = new BehaviorContext(now, dt, ctx, outbox, stateWithNeeds, Config, updatedCooldowns, new Dictionary<string, Characters.Engines.Memory.DecisionWorkingSet>(), _habitApplicabilityModulator, availableObjects);

            // Sleep can consume the entire tick because it owns a runtime session and prompt flow.
            var sleep = _sleepCoordinator.Tick(context);
            State = sleep.NewState;
            if (sleep.ConsumedTick) return;

            context = context with { State = State };
            var candidates = new List<BehaviorCandidate>();
            foreach (var engine in _needEngines) candidates.AddRange(engine.Evaluate(context).Candidates);
            ApplyDevelopmentGate(context, candidates);
            foreach (var modifier in _modifierEngines) modifier.Modify(context, candidates);

            _objectAffordanceGatingEngine.Modify(context, candidates);

            // Intent management stabilizes direction across ticks but still leaves final choice to arbitration.
            if (Config.UseIntentManagement)
            {
                State = _intentManagementEngine.UpdateIntent(context, candidates);
                context = context with { State = State };
                _intentManagementEngine.ApplyBias(context, candidates);
            }

            var result = _arbitrationEngine.Arbitrate(context, candidates);
            State = result.NewState;
            if (result.KeepRunningPlan || result.SelectedCandidate is null) return;

            // Commitment only tracks whether the final committed action supported the current intent.
            if (State.ActiveIntent is { } active)
            {
                var commitmentDelta = BehaviorIntentMapper.Matches(active, result.SelectedCandidate.Name) ? 1 : -1;
                State = State with
                {
                    ActiveIntent = active with
                    {
                        UpdatedAt = now,
                        Commitment = DefaultIntentManagementEngine.ClampCommitment(active.Commitment + commitmentDelta),
                        Strength = result.SelectedCandidate.Utility
                    }
                };
            }

            outbox.Add(new ActionProposed(now, ctx.Id, result.SelectedCandidate.Name, result.SelectedCandidate.Utility, result.SelectedCandidate.SocialTargeting?.TargetHuman, result.IntendedCandidate?.Name, result.ConflictReason));
            outbox.Add(new ActionCommitted(now, ctx.Id, result.SelectedCandidate.Name, result.SelectedCandidate.Duration, result.SelectedCandidate.SocialTargeting?.TargetHuman, result.IntendedCandidate?.Name, result.ConflictReason, result.SelectedCandidate.ObjectInteraction));
            EmitInteractionProposalIfNeeded(now, ctx, outbox, result.SelectedCandidate);
            SetCooldownsForCommittedAction(ctx.Id, result.SelectedCandidate.Name);
            using (_log.BeginCharacterScope(
                ctx.Id.Value,
                nameof(DefaultBehaviorEngine),
                relatedPersonId: result.SelectedCandidate.SocialTargeting?.TargetHuman.Value,
                tickKey: now.WorldTicks.ToString()))
            {
                _log.BehaviorActionChosen(ctx.Id.Value.ToString(), result.SelectedCandidate.Name, result.SelectedCandidate.Utility, result.SelectedCandidate.Duration.ToString());

                // Log object interaction detail when committing InteractWithObject actions
                if (result.SelectedCandidate.ObjectInteraction is { } oi)
                    _log.ObjectInteractionCommitted(ctx.Id.Value.ToString(), oi.ObjectId, oi.Kind.ToString(), oi.LocationId, result.SelectedCandidate.Utility);

                // Log explicit move actions so the location tracker can follow movement
                if (result.SelectedCandidate.Name.StartsWith("MoveTo:", StringComparison.OrdinalIgnoreCase))
                {
                    var currentLocation = ctx.Snapshot.InteractionSurface?.Location ?? string.Empty;
                    _log.MoveActionCommitted(ctx.Id.Value.ToString(), result.SelectedCandidate.Name, currentLocation, result.SelectedCandidate.Utility);
                }
            }
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is ActionCommitted committed)
            {
                State = BehaviorHabitLearning.LearnFromCommitment(State, committed, ctx, Config, _log);
            }

            State = _sleepCoordinator.Handle(@event, ctx, outbox, State);
        }

        public void RestoreState(BehaviorState state)
        { State = state; _sleepCoordinator.RestoreState(); }

        #endregion IEngine

        #region Cooldowns

        private void SetCooldownsForCommittedAction(HumanId owner, string chosen)
        {
            var hours = chosen switch { InviteIntimacy => 6, ReachOut => 4, _ => double.NaN };
            if (double.IsNaN(hours)) return;
            var dict = new Dictionary<string, double>(State.Cooldowns ?? new Dictionary<string, double>());
            dict[chosen] = hours;
            State = State with { Cooldowns = dict };
            _log.BehaviorCooldownSet(owner.Value.ToString(), chosen, hours);
        }

        private static void EmitInteractionProposalIfNeeded(WDateTime now, IHumanContext ctx, IEventCollector outbox, BehaviorCandidate candidate)
        {
            if (candidate.SocialTargeting is not { } targeting)
            {
                return;
            }

            if (candidate.Name is not (ReachOut or InviteIntimacy))
            {
                return;
            }

            outbox.Add(new InteractionProposed(now, ctx.Id, targeting.TargetHuman, targeting.SpeechAct, targeting.Reason, ctx.Biology));
        }

        private void ApplyDevelopmentGate(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var stadium = _developmentPolicy.ResolveStadium(context.HumanContext, context.Now);
            candidates.RemoveAll(candidate => !_developmentPolicy.AllowsAction(stadium, candidate.Name));
        }

        #endregion Cooldowns
    }
}
