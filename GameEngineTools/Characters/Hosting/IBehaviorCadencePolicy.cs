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
        /// <summary>Returns the minimum interval between behaviour decisions, or <c>null</c> for every tick.</summary>
        /// <param name="human">The character.</param>
        WTimeSpan? GetDecisionStep(IHuman human);
    }
}
