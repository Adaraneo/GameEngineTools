// SemanticMemory.Targeting.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;

    public enum SocialTargetMode
    { ReachOut, Vulnerability, Intimacy }

    public sealed record SocialTargetScore(
        HumanId Target,
        double Score,
        double ExpectedAcceptance,
        SpeechAct EvaluatedAct);

    public static class SemanticTargeting
    {
        public static SocialTargetScore ScoreTarget(
            IHuman initiator,
            IHuman target,
            SocialTargetMode mode)
        {
            var act = mode switch
            {
                SocialTargetMode.Intimacy => SpeechAct.Invite,
                SocialTargetMode.Vulnerability => SpeechAct.SelfDisclosure,
                _ => SpeechAct.SmallTalk
            };

            var relationship = initiator.Snapshot.Relationships.Edges.GetValueOrDefault(target.Id);
            var memory = initiator.Snapshot.Memory.Episodes;
            var expected = SemanticMemoryMath.ExpectedAcceptance(
                initiator.Snapshot.SemanticMemory,
                target.Id,
                act,
                relationship,
                initiator.PsychologyProfile,
                memory);

            var score = SemanticMemoryMath.ScoreApproachTarget(
                initiator.Snapshot.SemanticMemory,
                target.Id,
                relationship,
                initiator.PsychologyProfile,
                memory,
                act);

            return new SocialTargetScore(target.Id, score, expected, act);
        }

        public static IHuman? ChooseTarget(
            IHuman initiator,
            IReadOnlyList<IHuman> candidates,
            SocialTargetMode mode)
        {
            return candidates
                .Select(candidate => (Candidate: candidate, Score: ScoreTarget(initiator, candidate, mode)))
                .OrderByDescending(entry => entry.Score.Score)
                .ThenBy(entry => entry.Candidate.Id.Value)
                .Select(entry => entry.Candidate)
                .FirstOrDefault();
        }
    }
}
