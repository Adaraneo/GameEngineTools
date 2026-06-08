// IPerceptionFidelityPolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Resolves runtime perception fidelity.
    /// </summary>
    public interface IPerceptionFidelityPolicy
    {
        /// <summary>Returns the perception fidelity tier for a character.</summary>
        /// <param name="human">Character identifier.</param>
        PerceptionFidelityLevel GetLevel(HumanId human);
    }
}
