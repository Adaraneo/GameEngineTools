// GoalBehaviorModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Goals
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Applies flat utility bias to behavior candidates based on the character's active persistent goals.
    /// Goals nudge utility upward — they never subtract.
    /// </summary>
    internal sealed class GoalBehaviorModifier : IBehaviorModifierEngine
    {
        #region Private fields

        private readonly ILogger? _log;
        private readonly double _maxFlatBiasPerGoal;

        #endregion

        #region Construction

        public GoalBehaviorModifier(ILogger? log = null, double maxFlatBiasPerGoal = 12.0)
        {
            _log = log;
            _maxFlatBiasPerGoal = maxFlatBiasPerGoal;
        }

        #endregion

        #region IBehaviorModifierEngine

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var goals = context.HumanContext.Snapshot.Goals;
            if (goals is null || !goals.Active.GetEnumerator().MoveNext())
            {
                return;
            }

            var maxBias = _maxFlatBiasPerGoal;
            var humanId = context.HumanContext.Id.Value.ToString();

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var totalBias = 0.0;

                foreach (var goal in goals.Active)
                {
                    var bias = ComputeBias(goal, candidate, maxBias);
                    if (bias <= 0.0) continue;

                    totalBias += bias;

                    if (_log is not null)
                    {
                        using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(GoalBehaviorModifier)))
                        {
                            _log.GoalBiasApplied(humanId, candidate.Name, bias, goal.Kind.ToString(), goal.Salience);
                        }
                    }
                }

                if (totalBias > 0.0)
                {
                    candidates[i] = candidate with { Utility = Math.Max(0.0, candidate.Utility + totalBias) };
                }
            }
        }

        #endregion

        #region Private helpers

        private static double ComputeBias(PersistentGoal goal, BehaviorCandidate candidate, double maxBias)
        {
            var weight = GetWeight(goal, candidate);
            if (weight <= 0.0) return 0.0;
            return goal.Salience * weight * maxBias;
        }

        private static double GetWeight(PersistentGoal goal, BehaviorCandidate candidate)
        {
            var name = candidate.Name;
            var target = candidate.SocialTargeting?.TargetHuman;

            return goal.Kind switch
            {
                PersistentGoalKind.MasterCraft =>
                    name == Work ? 1.0 :
                    name == Create ? 0.75 :
                    0.0,

                PersistentGoalKind.BuildReputation =>
                    name == Work ? 0.4 :
                    name == ReachOut ? 0.5 :
                    0.0,

                PersistentGoalKind.FindMeaning =>
                    name == Create ? 0.8 :
                    name == Work ? 0.4 :
                    name == ReachOut ? 0.35 :
                    0.0,

                PersistentGoalKind.OvercomeTrauma =>
                    name == SelfCare ? 0.9 :
                    0.0,

                PersistentGoalKind.BuildIdentity =>
                    name == Create ? 0.8 :
                    name == Work ? 0.35 :
                    0.0,

                PersistentGoalKind.FindPartner =>
                    name == InviteIntimacy ? 1.0 :
                    name == ReachOut ? 0.45 :
                    0.0,

                PersistentGoalKind.RepairRelationship =>
                    name == ReachOut && goal.TargetHuman is not null && target == goal.TargetHuman ? 1.0 :
                    0.0,

                PersistentGoalKind.ProtectFamily =>
                    name == ReachOut && goal.TargetHuman is not null && target == goal.TargetHuman ? 0.8 :
                    0.0,

                PersistentGoalKind.EscapeDanger =>
                    name == MoveToPrivate ? 1.0 :
                    name == MoveToRest ? 0.6 :
                    0.0,

                PersistentGoalKind.SeekRevenge =>
                    name == ReachOut && goal.TargetHuman is not null && target == goal.TargetHuman ? 1.0 :
                    0.0,

                _ => 0.0
            };
        }

        #endregion
    }
}
