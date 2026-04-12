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
        PerceptionFidelityLevel GetLevel(HumanId human);
    }
}
