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
        private readonly double _shieldingCommitmentThreshold;
        private readonly double _shieldingMaxInhibition;
        private readonly double _shieldingStressDampening;

        #endregion Private fields

        #region Construction

        public GoalBehaviorModifier(
            ILogger? log = null,
            double maxFlatBiasPerGoal = 12.0,
            double shieldingCommitmentThreshold = 0.5,
            double shieldingMaxInhibition = 8.0,
            double shieldingStressDampening = 0.6)
        {
            _log = log;
            _maxFlatBiasPerGoal = maxFlatBiasPerGoal;
            _shieldingCommitmentThreshold = shieldingCommitmentThreshold;
            _shieldingMaxInhibition = shieldingMaxInhibition;
            _shieldingStressDampening = shieldingStressDampening;
        }

        #endregion Construction

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

            // Goal shielding (Shah, Friedman & Kruglanski 2002): a committed focal goal inhibits the
            // utility of actions that serve competing goals but do not facilitate the focal goal.
            ApplyGoalShielding(context, candidates, goals);
        }

        /// <summary>
        /// Inhibits the utility of competing-goal actions while a committed focal goal is active.
        /// Inhibition is stronger with focal commitment (salience), absent when the action also
        /// facilitates the focal goal, and weakened by stress (anxiety/depression proxy).
        /// Source: Shah, Friedman &amp; Kruglanski (2002, <i>JPSP</i> 83(6)).
        /// </summary>
        private void ApplyGoalShielding(
            BehaviorContext context, List<BehaviorCandidate> candidates, GoalState goals)
        {
            // The focal goal is the most-committed active goal above the commitment threshold.
            PersistentGoal? focal = null;
            foreach (var goal in goals.Active)
            {
                if (goal.Salience < _shieldingCommitmentThreshold) continue;
                if (focal is null || goal.Salience > focal.Salience) focal = goal;
            }
            if (focal is null) return;

            var stress = context.HumanContext.Snapshot.Psychology.Stress;
            var shieldFactor = Math.Clamp(1.0 - stress / 100.0 * _shieldingStressDampening, 0.0, 1.0);
            if (shieldFactor <= 0.0) return;

            var humanId = context.HumanContext.Id.Value.ToString();

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                // An action that facilitates the focal goal is never inhibited (Shah 2002).
                if (GetWeight(focal, candidate) > 0.0) continue;

                // Strength with which the candidate serves any competing goal.
                var competingWeight = 0.0;
                foreach (var goal in goals.Active)
                {
                    if (ReferenceEquals(goal, focal)) continue;
                    competingWeight = Math.Max(competingWeight, GetWeight(goal, candidate));
                }
                if (competingWeight <= 0.0) continue;

                var inhibition = focal.Salience * competingWeight * _shieldingMaxInhibition * shieldFactor;
                if (inhibition <= 0.0) continue;

                candidates[i] = candidate with { Utility = Math.Max(0.0, candidate.Utility - inhibition) };

                if (_log is not null)
                {
                    using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(GoalBehaviorModifier)))
                    {
                        _log.GoalBiasApplied(humanId, candidate.Name, -inhibition, $"shield:{focal.Kind}", focal.Salience);
                    }
                }
            }
        }

        #endregion IBehaviorModifierEngine

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

        #endregion Private helpers
    }
}
