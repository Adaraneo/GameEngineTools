// ISocialNormProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using GameEngineTools.Characters.Engines.Interactions;

    /// <summary>
    /// Provides <see cref="SocialNormContext"/> lookup by norm identifier.
    /// Implementations are expected to cache at startup — norms are immutable during runtime.
    /// </summary>
    public interface ISocialNormProvider
    {
        /// <summary>
        /// Returns the <see cref="SocialNormContext"/> for the given norm id,
        /// or <c>null</c> if the id is not found.
        /// </summary>
        SocialNormContext? GetNormContext(string normId);
    }
}
