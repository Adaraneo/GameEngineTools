// AutonomyExplorationNeedsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System.Collections.Generic;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    internal sealed class AutonomyExplorationNeedsEngine : IBehaviorNeedEngine
    {
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var novelty = 10 * context.HumanContext.Personality.Motivation.Curiosity;
            return new BehaviorNeedOutput(
                new[] { new BehaviorDrive("NeedNovelty", novelty, BehaviorDomain.Exploration) },
                new List<BehaviorCandidate>
                {
                    new BehaviorCandidate(MoveToPublic, 0, WTimeSpan.FromMinutes(20), BehaviorDomain.Exploration, new[] { "EnvironmentMovement" })
                });
        }
    }
}
