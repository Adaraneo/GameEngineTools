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
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Logging;
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

        #endregion Private fields

        #region Public properties

        public BehaviorState State { get; private set; }

        public BehaviorConfig Config { get; }

        #endregion Public properties

        #region Construction

        public DefaultBehaviorEngine(IOptions<BehaviorConfig> cfg, IOptions<SleepConfig> sleepCfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultBehaviorEngine>();
            State = new BehaviorState(40, 30, 25, 50, 50, 35, null, new Dictionary<string, double>());
            _needEngines = new IBehaviorNeedEngine[] { new PhysiologicalNeedsEngine(), new SocialNeedsEngine(), new CompetenceNeedsEngine(), new AutonomyExplorationNeedsEngine() };
            _modifierEngines = new IBehaviorModifierEngine[] { new TraitBiasEngine(), new PsychologicalConflictBiasEngine(), new AffectiveStateEngine(), new CircadianArousalEngine(), new HabitRoutineEngine(), new MemoryInfluenceEngine(), new EnvironmentalAffordanceEngine() };
            _sleepCoordinator = new DefaultSleepCoordinator(sleepCfg.Value, Config, loggerFactory);
            _intentManagementEngine = new DefaultIntentManagementEngine(loggerFactory.CreateLogger<DefaultIntentManagementEngine>());
            _arbitrationEngine = new DefaultActionArbitrationEngine(loggerFactory.CreateLogger<DefaultActionArbitrationEngine>());
        }

        #endregion Construction

        #region IEngine

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // Build the tick-local behavior context before delegating to sub-engines.
            var updatedCooldowns = BehaviorMath.UpdateCooldowns(State.Cooldowns, Math.Max(0, dt.TotalHours));
            var stateWithNeeds = BehaviorMath.ComputeNeedState(ctx, updatedCooldowns, State) with { Cooldowns = updatedCooldowns };
            var context = new BehaviorContext(now, dt, ctx, outbox, stateWithNeeds, Config, updatedCooldowns, new Dictionary<string, Characters.Engines.Memory.DecisionWorkingSet>());

            // Sleep can consume the entire tick because it owns a runtime session and prompt flow.
            var sleep = _sleepCoordinator.Tick(context);
            State = sleep.NewState;
            if (sleep.ConsumedTick) return;

            context = context with { State = State };
            var candidates = new List<BehaviorCandidate>();
            foreach (var engine in _needEngines) candidates.AddRange(engine.Evaluate(context).Candidates);
            foreach (var modifier in _modifierEngines) modifier.Modify(context, candidates);

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
            outbox.Add(new ActionCommitted(now, ctx.Id, result.SelectedCandidate.Name, result.SelectedCandidate.Duration, result.SelectedCandidate.SocialTargeting?.TargetHuman, result.IntendedCandidate?.Name, result.ConflictReason));
            EmitInteractionProposalIfNeeded(now, ctx, outbox, result.SelectedCandidate);
            SetCooldownsForCommittedAction(ctx.Id, result.SelectedCandidate.Name);
            using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultBehaviorEngine)))) _log.BehaviorActionChosen(ctx.Id.Value.ToString(), result.SelectedCandidate.Name, result.SelectedCandidate.Utility, result.SelectedCandidate.Duration.ToString());
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox) => State = _sleepCoordinator.Handle(@event, ctx, outbox, State);
        public void RestoreState(BehaviorState state) { State = state; _sleepCoordinator.RestoreState(); }

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

        #endregion Cooldowns
    }
}
