// SocialNeedsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System.Collections.Generic;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Generates affiliation and intimacy pressure together with socially relevant movement.
    /// </summary>
    internal sealed class SocialNeedsEngine : IBehaviorNeedEngine
    {
        #region IBehaviorNeedEngine

        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var m = context.HumanContext.Personality.Motivation;
            var candidates = new List<BehaviorCandidate>
            {
                new BehaviorCandidate(MoveToSocial, context.State.NeedBelonging * m.Affiliation, WTimeSpan.FromMinutes(20), BehaviorDomain.Social, new[] { "EnvironmentMovement" }),
                new BehaviorCandidate(MoveToPrivate, 0, WTimeSpan.FromMinutes(20), BehaviorDomain.Social, new[] { "EnvironmentMovement" })
            };
            candidates.AddRange(SocialTargetCandidateFactory.Create(context));

            return new BehaviorNeedOutput(
                new[]
                {
                    new BehaviorDrive(nameof(context.State.NeedBelonging), context.State.NeedBelonging, BehaviorDomain.Social),
                    new BehaviorDrive(nameof(context.State.NeedIntimacy), context.State.NeedIntimacy, BehaviorDomain.Social)
                },
                candidates);
        }

        #endregion IBehaviorNeedEngine
    }
}
