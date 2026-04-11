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
        SocialFidelityLevel GetLevel(HumanId human);
    }
}
