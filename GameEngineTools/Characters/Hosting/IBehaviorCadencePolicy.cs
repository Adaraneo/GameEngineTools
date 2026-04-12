// IBehaviorCadencePolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.World.Utils.Time;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Resolves the current behavior decision cadence for a character.
    /// </summary>
    public interface IBehaviorCadencePolicy
    {
        WTimeSpan? GetDecisionStep(IHuman human);
    }
}
