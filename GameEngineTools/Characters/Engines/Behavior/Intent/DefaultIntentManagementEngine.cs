// DefaultIntentManagementEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Intent
{
    using System.Linq;
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;

    internal sealed class DefaultIntentManagementEngine : IIntentManagementEngine
    {
        private readonly ILogger _log;
        public DefaultIntentManagementEngine(ILogger log) => _log = log;

        public BehaviorState UpdateIntent(BehaviorContext context, IReadOnlyList<BehaviorCandidate> candidates)
        {
            var current = context.State.ActiveIntent;
            var grouped = candidates.GroupBy(c => BehaviorIntentMapper.Resolve(c.Name)).ToDictionary(g => g.Key, g => g.Max(c => c.Utility));
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
                var currentScore = grouped.TryGetValue(current.Kind, out var score) ? score : double.MinValue;
                if (expired || currentScore == double.MinValue)
                {
                    using (_log.BeginScope(new CharacterLogScope(context.HumanContext.Id.Value, nameof(DefaultIntentManagementEngine)))) _log.LogDebug("intent expired {Intent}", current.Kind);
                    current = null;
                }
            }

            var winning = grouped.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (current is null)
            {
                if (winning.Key == BehaviorIntentKind.None && winning.Value == 0) return context.State with { ActiveIntent = null };
                var selected = NewIntent(context, candidates.First(c => BehaviorIntentMapper.Resolve(c.Name) == winning.Key).Name, winning.Value);
                Log("intent selected", context, selected);
                return context.State with { ActiveIntent = selected };
            }

            var currentIntentScore = grouped.TryGetValue(current.Kind, out var currentScore2) ? currentScore2 : 0;
            if (winning.Key != current.Kind && winning.Value > currentIntentScore + context.Config.IntentSwitchMargin)
            {
                var switched = NewIntent(context, candidates.First(c => BehaviorIntentMapper.Resolve(c.Name) == winning.Key).Name, winning.Value);
                Log("intent switched", context, switched);
                return context.State with { ActiveIntent = switched };
            }

            var retained = current with { UpdatedAt = context.Now, Strength = currentIntentScore, ExpiresAt = context.Now + GameEngineTools.World.Utils.Time.WTimeSpan.FromHours(context.Config.IntentTimeoutHours) };
            Log("intent retained", context, retained);
            return context.State with { ActiveIntent = retained };
        }

        public void ApplyBias(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var intent = context.State.ActiveIntent;
            if (intent is null || intent.Kind == BehaviorIntentKind.None) return;
            var bias = context.Config.IntentBaseBias + (intent.Commitment * context.Config.IntentCommitmentBiasStep);
            for (var i = 0; i < candidates.Count; i++)
            {
                if (!BehaviorIntentMapper.Matches(intent, candidates[i].Name)) continue;
                candidates[i] = candidates[i] with { Utility = candidates[i].Utility + bias };
                using (_log.BeginScope(new CharacterLogScope(context.HumanContext.Id.Value, nameof(DefaultIntentManagementEngine)))) _log.LogDebug("bias applied {Action} {Bias}", candidates[i].Name, bias);
            }
        }

        private static ActiveIntent NewIntent(BehaviorContext context, string actionName, double strength)
            => new(BehaviorIntentMapper.Resolve(actionName), actionName, context.Now, context.Now, strength, 0, context.Now + GameEngineTools.World.Utils.Time.WTimeSpan.FromHours(context.Config.IntentTimeoutHours));

        private void Log(string message, BehaviorContext context, ActiveIntent intent)
        {
            using (_log.BeginScope(new CharacterLogScope(context.HumanContext.Id.Value, nameof(DefaultIntentManagementEngine))))
                _log.LogDebug("{Message} {IntentKind} -> {Action}", message, intent.Kind, intent.TargetAction);
        }
    }
}
