// DefaultActionArbitrationEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Arbitration
{
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Resolves the final action choice after needs, modifiers, and intent shaping have run.
    /// </summary>
    internal sealed class DefaultActionArbitrationEngine : IActionArbitrationEngine
    {
        #region Private fields

        private readonly ILogger _log;

        #endregion Private fields

        #region Construction

        public DefaultActionArbitrationEngine(ILogger log) => _log = log;

        #endregion Construction

        #region IActionArbitrationEngine

        public ActionArbitrationResult Arbitrate(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            if (context.State.CurrentPlan is { } running)
            {
                var elapsed = context.Now - running.Start;
                if (elapsed < running.ExpectedDuration)
                {
                    // A severe hunger or thirst interrupts a running plan; ordinary needs wait until it has run its course.
                    if (FindPreemptingCandidate(context, candidates, running) is { } emergency)
                    {
                        var preemptPlan = new PlannedAction(emergency.Name, context.Now, emergency.Duration, emergency.Utility, emergency.SocialTargeting?.TargetHuman, emergency.ObjectInteraction);
                        return new ActionArbitrationResult(false, preemptPlan, emergency, context.State with { CurrentPlan = preemptPlan, Cooldowns = context.Cooldowns }, emergency, $"preempted:{running.Name}");
                    }

                    // Keep the current plan alive until its expected window has elapsed.
                    using (_log.BeginCharacterScope(
                        context.HumanContext.Id.Value,
                        nameof(DefaultActionArbitrationEngine),
                        relatedPersonId: running.TargetHuman?.Value,
                        tickKey: context.Now.WorldTicks.ToString()))
                        _log.BehaviorActionRunning(context.HumanContext.Id.Value.ToString(), running.Name, (running.ExpectedDuration - elapsed).ToString());
                    return new ActionArbitrationResult(true, running, null, context.State with { CurrentPlan = running, Cooldowns = context.Cooldowns });
                }
            }

            if (candidates.Count == 0)
                return new ActionArbitrationResult(false, null, null, context.State with { Cooldowns = context.Cooldowns });

            candidates.Sort((a, b) => b.Utility.CompareTo(a.Utility));
            var intended = candidates[0];
            var chosen = ChooseWithConflict(context, candidates, intended, out var reason);
            var plan = new PlannedAction(chosen.Name, context.Now, chosen.Duration, chosen.Utility, chosen.SocialTargeting?.TargetHuman, chosen.ObjectInteraction);
            return new ActionArbitrationResult(false, plan, chosen, context.State with { CurrentPlan = plan, Cooldowns = context.Cooldowns }, intended, reason);
        }

        #endregion IActionArbitrationEngine

        /// <summary>
        /// The candidate that answers a severe hunger or thirst while an unrelated plan is running, or <c>null</c>. The severity is the raw need
        /// (<see cref="BehaviorConfig.EmergencyIntentOverrideThreshold"/>), not a modifier-adjusted utility: utilities are need x 1.6-1.7, so the
        /// utility threshold is crossed at moderate hunger, and discounting could pull a genuine emergency below it.
        /// </summary>
        private static BehaviorCandidate? FindPreemptingCandidate(BehaviorContext context, List<BehaviorCandidate> candidates, PlannedAction running)
        {
            var threshold = context.Config.EmergencyIntentOverrideThreshold;
            var answersHunger = context.State.NeedFood >= threshold;
            var answersThirst = context.State.NeedWater >= threshold;
            if (!answersHunger && !answersThirst) return null;

            bool Answers(string action)
                => (answersHunger && action is Eat or EatStored or MoveToFood) || (answersThirst && action is Drink or MoveToDrink);

            // Already responding to the emergency: let the plan finish.
            if (Answers(running.Name)) return null;

            return candidates
                .Where(c => c.Utility > 0 && Answers(c.Name))
                .OrderByDescending(c => c.Utility)
                .FirstOrDefault();
        }

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
