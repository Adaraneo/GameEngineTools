// ISocialFidelityPolicy.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Resolves runtime social fidelity.
    /// </summary>
    public interface ISocialFidelityPolicy
    {
        /// <summary>Returns the social fidelity tier for a character.</summary>
        /// <param name="human">Character identifier.</param>
        SocialFidelityLevel GetLevel(HumanId human);
    }
}
