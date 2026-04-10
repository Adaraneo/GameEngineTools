// DefaultActionArbitrationEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Arbitration
{
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Resolves the final action choice after needs, modifiers, and intent shaping have run.
    /// </summary>
    internal sealed class DefaultActionArbitrationEngine : IActionArbitrationEngine
    {
        #region Private fields

        private readonly ILogger _log;

        #endregion

        #region Construction

        public DefaultActionArbitrationEngine(ILogger log) => _log = log;

        #endregion

        #region IActionArbitrationEngine

        public ActionArbitrationResult Arbitrate(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            if (context.State.CurrentPlan is { } running)
            {
                var elapsed = context.Now - running.Start;
                if (elapsed < running.ExpectedDuration)
                {
                    // Keep the current plan alive until its expected window has elapsed.
                    using (_log.BeginScope(new CharacterLogScope(context.HumanContext.Id.Value, nameof(DefaultActionArbitrationEngine))))
                        _log.BehaviorActionRunning(context.HumanContext.Id.Value.ToString(), running.Name, (running.ExpectedDuration - elapsed).ToString());
                    return new ActionArbitrationResult(true, running, null, context.State with { CurrentPlan = running, Cooldowns = context.Cooldowns });
                }
            }

            if (candidates.Count == 0)
                return new ActionArbitrationResult(false, null, null, context.State with { Cooldowns = context.Cooldowns });

            candidates.Sort((a, b) => b.Utility.CompareTo(a.Utility));
            var intended = candidates[0];
            var chosen = ChooseWithConflict(context, candidates, intended, out var reason);
            var plan = new PlannedAction(chosen.Name, context.Now, chosen.Duration, chosen.Utility, chosen.SocialTargeting?.TargetHuman);
            return new ActionArbitrationResult(false, plan, chosen, context.State with { CurrentPlan = plan, Cooldowns = context.Cooldowns }, intended, reason);
        }

        #endregion

        private static BehaviorCandidate ChooseWithConflict(BehaviorContext context, List<BehaviorCandidate> candidates, BehaviorCandidate intended, out string? reason)
        {
            var profile = context.HumanContext.PsychologyProfile;
            var ambivalence = Math.Clamp(profile.Ambivalence + (context.HumanContext.Snapshot.Psychology.Stress / 100.0) * 0.25, 0.0, 1.0);
            var topDelta = candidates.Count > 1 ? intended.Utility - candidates[1].Utility : intended.Utility;
            var tension = Math.Clamp(1.0 - (topDelta / 20.0), 0.0, 1.0);
            var conflictChance = ambivalence * tension * (1.0 - profile.FollowThrough * 0.5);

            if (candidates.Count > 1 && context.HumanContext.Random.Chance(conflictChance))
            {
                var conflicted = candidates[1];
                reason = $"wanted:{intended.Name}->did:{conflicted.Name}";
                return conflicted;
            }

            reason = null;
            return intended;
        }
    }
}
