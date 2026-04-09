// BehaviorCandidateEditor.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    /// <summary>
    /// Centralizes simple candidate mutations so modifier engines stay declarative.
    /// </summary>
    internal static class BehaviorCandidateEditor
    {
        #region Utility edits

        internal static void Multiply(List<BehaviorCandidate> candidates, string name, double multiplier)
        {
            for (var i = 0; i < candidates.Count; i++) if (candidates[i].Name == name) candidates[i] = candidates[i] with { Utility = Math.Max(0, candidates[i].Utility * multiplier) };
        }

        internal static void Add(List<BehaviorCandidate> candidates, string name, double value)
        {
            for (var i = 0; i < candidates.Count; i++) if (candidates[i].Name == name) candidates[i] = candidates[i] with { Utility = Math.Max(0, candidates[i].Utility + value) };
        }

        #endregion

        #region Tags

        internal static bool HasTag(BehaviorCandidate candidate, string tag) => candidate.Tags?.Contains(tag) == true;

        #endregion
    }
}
