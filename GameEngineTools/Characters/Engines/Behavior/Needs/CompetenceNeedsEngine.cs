// CompetenceNeedsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System.Collections.Generic;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Generates productive and mastery-oriented pressure from competence needs.
    /// </summary>
    internal sealed class CompetenceNeedsEngine : IBehaviorNeedEngine
    {
        #region IBehaviorNeedEngine

        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var m = context.HumanContext.Personality.Motivation;
            return new BehaviorNeedOutput(
                new[] { new BehaviorDrive(nameof(context.State.NeedCompetence), context.State.NeedCompetence, BehaviorDomain.Competence) },
                new List<BehaviorCandidate>
                {
                    new BehaviorCandidate(Work, BehaviorMath.Util(context.State.NeedCompetence, m.Competence), WTimeSpan.FromHours(2.0), BehaviorDomain.Competence, new[] { "ProductiveSurface" }),
                    new BehaviorCandidate(Create, BehaviorMath.Util(context.State.NeedCompetence, m.Curiosity), WTimeSpan.FromHours(1.5), BehaviorDomain.Competence, new[] { "ProductiveSurface" }),
                    new BehaviorCandidate(MoveToWork, 0, WTimeSpan.FromMinutes(20), BehaviorDomain.Competence, new[] { "EnvironmentMovement" })
                });
        }

        #endregion
    }
}
