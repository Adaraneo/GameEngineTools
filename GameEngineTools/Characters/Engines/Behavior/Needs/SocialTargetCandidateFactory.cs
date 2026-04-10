// SocialTargetCandidateFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    internal static class SocialTargetCandidateFactory
    {
        internal static IReadOnlyList<BehaviorCandidate> Create(BehaviorContext context)
        {
            var candidates = new List<BehaviorCandidate>();
            var motivation = context.HumanContext.Personality.Motivation;
            var knownTargets = KnownTargets(context);

            foreach (var target in SemanticTargeting.RankTargets(context.HumanContext, knownTargets, SocialTargetMode.ReachOut))
            {
                candidates.Add(BuildReachOutCandidate(context, target, motivation.Affiliation));
            }

            foreach (var target in SemanticTargeting.RankTargets(context.HumanContext, knownTargets, SocialTargetMode.Intimacy))
            {
                if (target.PsychologicallyBlocked)
                {
                    continue;
                }

                candidates.Add(BuildInviteIntimacyCandidate(context, target, motivation.Sexuality));
            }

            return candidates;
        }

        private static BehaviorCandidate BuildReachOutCandidate(BehaviorContext context, SocialTargetScore target, double affiliation)
        {
            var utility = BehaviorMath.Util(context.State.NeedBelonging, affiliation)
                * (0.45 + target.Score * 0.55)
                + target.ExpectedAcceptance * 12.0;

            return new BehaviorCandidate(
                ReachOut,
                utility,
                WTimeSpan.FromHours(1.0),
                BehaviorDomain.Social,
                new[] { "TargetedSocial", "MemoryShapedSocial" },
                new SocialTargetingData(target.Target, target.EvaluatedAct, target.ExpectedAcceptance, target.VulnerabilitySafety, target.RejectionRisk, target.PsychologicallyBlocked, target.Reason));
        }

        private static BehaviorCandidate BuildInviteIntimacyCandidate(BehaviorContext context, SocialTargetScore target, double sexuality)
        {
            var utility = BehaviorMath.Util(context.State.NeedIntimacy, sexuality)
                * (0.35 + target.Score * 0.65)
                + target.VulnerabilitySafety * 10.0
                - target.RejectionRisk * 6.0;

            return new BehaviorCandidate(
                InviteIntimacy,
                utility,
                WTimeSpan.FromHours(1.0),
                BehaviorDomain.Social,
                new[] { "TargetedSocial", "MemoryShapedSocial", "PrivateSurface" },
                new SocialTargetingData(target.Target, target.EvaluatedAct, target.ExpectedAcceptance, target.VulnerabilitySafety, target.RejectionRisk, target.PsychologicallyBlocked, target.Reason));
        }

        private static IReadOnlyList<HumanId> KnownTargets(BehaviorContext context)
        {
            var relationshipTargets = context.HumanContext.Snapshot.Relationships.Edges.Keys;
            var semanticTargets = context.HumanContext.Snapshot.SemanticMemory?.People.Keys ?? Enumerable.Empty<HumanId>();
            var memoryTargets = context.HumanContext.Snapshot.Memory.Episodes.Where(e => e.OtherPerson.HasValue).Select(e => e.OtherPerson!.Value);
            return relationshipTargets.Concat(semanticTargets).Concat(memoryTargets).Distinct().ToList();
        }
    }
}
