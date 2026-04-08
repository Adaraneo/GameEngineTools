// BehaviorCandidateEditor.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    internal static class BehaviorCandidateEditor
    {
        internal static void Multiply(List<BehaviorCandidate> candidates, string name, double multiplier)
        {
            for (var i = 0; i < candidates.Count; i++) if (candidates[i].Name == name) candidates[i] = candidates[i] with { Utility = Math.Max(0, candidates[i].Utility * multiplier) };
        }
        internal static void Add(List<BehaviorCandidate> candidates, string name, double value)
        {
            for (var i = 0; i < candidates.Count; i++) if (candidates[i].Name == name) candidates[i] = candidates[i] with { Utility = Math.Max(0, candidates[i].Utility + value) };
        }
        internal static bool HasTag(BehaviorCandidate candidate, string tag) => candidate.Tags?.Contains(tag) == true;
    }
}
