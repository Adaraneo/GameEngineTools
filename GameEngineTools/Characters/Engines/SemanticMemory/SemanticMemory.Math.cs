// SemanticMemory.Math.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;

    public static class SemanticMemoryMath
    {
        public static double ExpectedAcceptance(
            SemanticMemoryState? state,
            HumanId other,
            SpeechAct act)
        {
            if (state is null)
            {
                return 0.5;
            }

            var warm = state.GetStrength(other, PersonBeliefKind.Warm);
            var safe = state.GetStrength(other, PersonBeliefKind.EmotionallySafe);
            var reliable = state.GetStrength(other, PersonBeliefKind.Reliable);
            var rejecting = state.GetStrength(other, PersonBeliefKind.Rejecting);
            var critical = state.GetStrength(other, PersonBeliefKind.Critical);

            var vulnerabilityWeight = act switch
            {
                SpeechAct.SelfDisclosure => 1.25,
                SpeechAct.Meta => 1.10,
                SpeechAct.Invite => 1.20,
                SpeechAct.Validation => 1.0,
                _ => 0.8
            };

            var positive = warm * 0.28 + safe * 0.32 * vulnerabilityWeight + reliable * 0.22;
            var negative = rejecting * 0.34 * vulnerabilityWeight + critical * 0.24;
            return Math.Clamp(0.5 + positive - negative, 0.05, 0.95);
        }
    }
}
