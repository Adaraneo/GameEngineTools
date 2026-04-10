// MemoryInfluenceEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using GameEngineTools.Characters.Engines.Memory;
    using static ActionNames;

    /// <summary>
    /// Replays emotionally salient episodes into current candidate utilities.
    /// </summary>
    internal sealed class MemoryInfluenceEngine : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            var episodes = context.HumanContext.Snapshot.Memory.Episodes;
            if (episodes.Count == 0) return;

            // Social memory can either dampen or reinforce outreach based on recent emotional tone.
            var negativeInteractions = episodes.Where(e => Perceived(e).Contains("Interaction:") && e.Emotion == EmotionalTag.Negative && e.Strength > 0.4).ToList();
            foreach (var e in negativeInteractions) context.Outbox.Add(new MemoryRecalled(context.Now, context.HumanContext.Id, e.Id));
            if (negativeInteractions.Count > 0) BehaviorCandidateEditor.Multiply(candidates, ReachOut, 1.0 - Math.Min(0.55, negativeInteractions.Sum(e => 0.08 + e.Distortion * 0.12)));

            var positiveInteractions = episodes.Where(e => Perceived(e).Contains("Interaction:") && e.Emotion == EmotionalTag.Positive && e.Strength > 0.4).ToList();
            foreach (var e in positiveInteractions) context.Outbox.Add(new MemoryRecalled(context.Now, context.HumanContext.Id, e.Id));
            if (positiveInteractions.Count > 0) BehaviorCandidateEditor.Multiply(candidates, ReachOut, 1.0 + Math.Min(0.25, positiveInteractions.Count * 0.08));

            var rejectedIntimacy = episodes.Where(e => Perceived(e).Contains("InviteIntimacy") && e.Emotion == EmotionalTag.Negative && e.Strength > 0.35).ToList();
            foreach (var e in rejectedIntimacy) context.Outbox.Add(new MemoryRecalled(context.Now, context.HumanContext.Id, e.Id));
            if (rejectedIntimacy.Count > 0) BehaviorCandidateEditor.Multiply(candidates, InviteIntimacy, 1.0 - Math.Min(0.65, rejectedIntimacy.Sum(e => 0.16 + e.Distortion * 0.15)));

            var negativeLoad = episodes.Where(e => e.Emotion == EmotionalTag.Negative && e.Strength > 0.3).ToList();
            foreach (var e in negativeLoad) context.Outbox.Add(new MemoryRecalled(context.Now, context.HumanContext.Id, e.Id));
            var loadSum = negativeLoad.Sum(e => e.Strength);
            if (loadSum > 0.5) BehaviorCandidateEditor.Multiply(candidates, SelfCare, 1.0 + Math.Min(0.35, loadSum * 0.08));

            var threateningMemories = episodes.Where(e => Perceived(e).StartsWith("PerceivedThreat:", StringComparison.Ordinal)).ToList();
            if (threateningMemories.Count > 0)
            {
                BehaviorCandidateEditor.Multiply(candidates, ReachOut, 1.0 - Math.Min(0.45, threateningMemories.Count * 0.08));
                BehaviorCandidateEditor.Multiply(candidates, SelfCare, 1.0 + Math.Min(0.25, threateningMemories.Count * 0.06));
            }
        }

        #endregion

        private static string Perceived(EpisodicMemory episode) => episode.PerceivedWhat ?? episode.What;
    }
}
