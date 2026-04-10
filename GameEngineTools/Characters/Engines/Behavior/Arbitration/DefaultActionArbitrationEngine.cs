// DefaultActionArbitrationEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Arbitration
{
    using System.Linq;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;
    using static ActionNames;

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
            var plan = new PlannedAction(chosen.Name, context.Now, chosen.Duration, chosen.Utility);
            return new ActionArbitrationResult(false, plan, chosen, context.State with { CurrentPlan = plan, Cooldowns = context.Cooldowns }, intended, reason);
        }

        #endregion

        private static BehaviorCandidate ChooseWithConflict(BehaviorContext context, List<BehaviorCandidate> candidates, BehaviorCandidate intended, out string? reason)
        {
            var profile = context.HumanContext.PsychologyProfile;
            var stress = context.HumanContext.Snapshot.Psychology.Stress / 100.0;
            var ambivalence = Math.Clamp(profile.Ambivalence + stress * 0.25, 0.0, 1.0);
            var topDelta = candidates.Count > 1 ? intended.Utility - candidates[1].Utility : intended.Utility;
            var tension = Math.Clamp(1.0 - (topDelta / 20.0), 0.0, 1.0);
            var conflictChance = ambivalence * tension * (1.0 - profile.FollowThrough * 0.5);

            var resolved = candidates
                .Select(candidate => new { Candidate = candidate, Score = candidate.Utility + IdentityBias(profile, candidate) + CopingBias(profile, stress, candidate) })
                .OrderByDescending(x => x.Score)
                .First()
                .Candidate;

            if (resolved.Name != intended.Name && context.HumanContext.Random.Chance(conflictChance))
            {
                reason = $"wanted:{intended.Name}->did:{resolved.Name}";
                return resolved;
            }

            reason = null;
            return intended;
        }

        private static double IdentityBias(PsychologicalProfile profile, BehaviorCandidate candidate)
            => candidate.Name switch
            {
                Work or Create => profile.Narrative.DiligenceIdentity * 8.0,
                SelfCare => (1.0 - profile.Narrative.ToughnessIdentity) * 6.0,
                ReachOut or MoveToSocial => profile.Narrative.BelongingIdentity * 7.0,
                InviteIntimacy => profile.Narrative.BelongingIdentity * 4.0,
                _ => 0.0
            };

        private static double CopingBias(PsychologicalProfile profile, double stress, BehaviorCandidate candidate)
            => profile.Coping switch
            {
                CopingStyle.Avoidant when candidate.Name is ReachOut or InviteIntimacy => -(4.0 + stress * 6.0),
                CopingStyle.Avoidant when candidate.Name is SelfCare or Idle => 2.0 + stress * 3.0,
                CopingStyle.PeoplePleasing when candidate.Name is ReachOut or MoveToSocial => 3.0 + stress * 3.0,
                CopingStyle.Rationalizing when candidate.Name is Work or Create => 2.5 + stress * 2.0,
                CopingStyle.Humor when candidate.Name is MoveToSocial or ReachOut => 2.0,
                CopingStyle.AggressiveCompensation when candidate.Name is Work => 3.0 + stress * 4.0,
                CopingStyle.AggressiveCompensation when candidate.Name is SelfCare => -2.5,
                _ => 0.0
            };
    }
}
