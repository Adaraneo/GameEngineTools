// AutonomyExplorationNeedsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System.Collections.Generic;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Produces low-pressure novelty and wandering behavior from autonomy and curiosity.
    /// </summary>
    internal sealed class AutonomyExplorationNeedsEngine : IBehaviorNeedEngine
    {
        #region IBehaviorNeedEngine

        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var p = context.HumanContext.Personality;
            // Openness amplifies the base curiosity drive by up to 50%.
            // Curiosity (Motivation) is the short-term drive; Openness (BigFive) is the stable trait.
            var novelty = 10 * p.Motivation.Curiosity * (0.5 + 0.5 * p.BigFive.Openness);
            return new BehaviorNeedOutput(
                new[] { new BehaviorDrive("NeedNovelty", novelty, BehaviorDomain.Exploration) },
                new List<BehaviorCandidate>
                {
                    new BehaviorCandidate(MoveToPublic, 0, WTimeSpan.FromMinutes(20), BehaviorDomain.Exploration, new[] { "EnvironmentMovement" })
                });
        }

        #endregion
    }
}
