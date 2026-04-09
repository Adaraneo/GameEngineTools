// DefaultIntentManagementEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Intent
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;

    internal sealed class DefaultIntentManagementEngine : IIntentManagementEngine
    {
        internal const int MinCommitment = 0;
        internal const int MaxCommitment = 10;

        private readonly ILogger _log;
        public DefaultIntentManagementEngine(ILogger log) => _log = log;

        public BehaviorState UpdateIntent(BehaviorContext context, IReadOnlyList<BehaviorCandidate> candidates)
        {
            var current = context.State.ActiveIntent;
            var scoredGroups = ScoreGroups(candidates);
            var emergency = candidates.Where(c => c.Domain == BehaviorDomain.Physiological && c.Utility >= context.Config.EmergencyIntentOverrideThreshold).OrderByDescending(c => c.Utility).FirstOrDefault();
            if (emergency is not null)
            {
                var overrideIntent = NewIntent(context, emergency.Name, emergency.Utility);
                Log("emergency override", context, overrideIntent);
                return context.State with { ActiveIntent = overrideIntent };
            }

            if (current is not null)
            {
                var expired = current.ExpiresAt.HasValue && context.Now >= current.ExpiresAt.Value;
                var currentScore = scoredGroups.TryGetValue(current.Kind, out var score) ? score : double.MinValue;
                if (expired || currentScore == double.MinValue)
                {
                    using (_log.BeginScope(new CharacterLogScope(context.HumanContext.Id.Value, nameof(DefaultIntentManagementEngine)))) _log.LogDebug("intent expired {Intent}", current.Kind);
                    current = null;
                }
            }

            var winning = scoredGroups.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (current is null)
            {
                if (winning.Key == BehaviorIntentKind.None || winning.Value <= 0)
                    return context.State with { ActiveIntent = null };

                var strongest = HighestCandidateForKind(candidates, winning.Key);
                var selected = NewIntent(context, strongest.Name, winning.Value);
                Log("intent selected", context, selected);
                return context.State with { ActiveIntent = selected };
            }

            var currentIntentScore = scoredGroups.TryGetValue(current.Kind, out var currentScore2) ? currentScore2 : 0;
            if (winning.Key != current.Kind && winning.Key != BehaviorIntentKind.None && winning.Value > currentIntentScore + context.Config.IntentSwitchMargin)
            {
                var strongest = HighestCandidateForKind(candidates, winning.Key);
                var switched = NewIntent(context, strongest.Name, winning.Value);
                Log("intent switched", context, switched);
                return context.State with { ActiveIntent = switched };
            }

            var retainedTarget = HighestCandidateForKind(candidates, current.Kind)?.Name ?? current.TargetAction;
            var retained = current with
            {
                TargetAction = retainedTarget,
                UpdatedAt = context.Now,
                Strength = currentIntentScore,
                ExpiresAt = context.Now + WTimeSpan.FromHours(context.Config.IntentTimeoutHours)
            };
            Log("intent retained", context, retained);
            return context.State with { ActiveIntent = retained };
        }

        public void ApplyBias(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var intent = context.State.ActiveIntent;
            if (intent is null || intent.Kind == BehaviorIntentKind.None) return;
            if (intent.Commitment <= 0) return;
            var bias = Math.Max(0.0, context.Config.IntentBaseBias + (intent.Commitment * context.Config.IntentCommitmentBiasStep));
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!BehaviorIntentMapper.Matches(intent, candidates[i].Name)) continue;
                candidates[i] = candidates[i] with { Utility = candidates[i].Utility + bias };
                using (_log.BeginScope(new CharacterLogScope(context.HumanContext.Id.Value, nameof(DefaultIntentManagementEngine)))) _log.LogDebug("bias applied {Action} {Bias}", candidates[i].Name, bias);
            }
        }

        internal static int ClampCommitment(int commitment)
            => Math.Clamp(commitment, MinCommitment, MaxCommitment);

        internal static IReadOnlyDictionary<BehaviorIntentKind, double> ScoreGroups(IReadOnlyList<BehaviorCandidate> candidates)
            => candidates
                .GroupBy(c => BehaviorIntentMapper.Resolve(c.Name))
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var utilities = g.Select(c => c.Utility).OrderByDescending(v => v).ToArray();
                        return utilities.Length == 0 ? 0.0 : utilities[0] + (utilities.Length > 1 ? 0.35 * utilities[1] : 0.0);
                    });

        private static BehaviorCandidate? HighestCandidateForKind(IReadOnlyList<BehaviorCandidate> candidates, BehaviorIntentKind kind)
            => candidates
                .Where(c => BehaviorIntentMapper.Resolve(c.Name) == kind)
                .OrderByDescending(c => c.Utility)
                .FirstOrDefault();

        private static ActiveIntent NewIntent(BehaviorContext context, string actionName, double strength)
            => new(BehaviorIntentMapper.Resolve(actionName), actionName, context.Now, context.Now, strength, MinCommitment, context.Now + WTimeSpan.FromHours(context.Config.IntentTimeoutHours));

        private void Log(string message, BehaviorContext context, ActiveIntent intent)
        {
            using (_log.BeginScope(new CharacterLogScope(context.HumanContext.Id.Value, nameof(DefaultIntentManagementEngine))))
                _log.LogDebug("{Message} {IntentKind} -> {Action}", message, intent.Kind, intent.TargetAction);
        }
    }
}
