// SexualOrientation.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>
    /// Stable sexual orientation category used by attraction generation.
    /// </summary>
    /// <remarks>
    /// Sociosexuality controls how readily sexual motivation is expressed.
    /// Sexual orientation controls which target sex categories tend to elicit attraction.
    /// </remarks>
    public enum SexualOrientation
    {
        /// <summary>Primarily attracted to a different binary sex category.</summary>
        Heterosexual,

        /// <summary>Primarily attracted to the same binary sex category.</summary>
        Homosexual,

        /// <summary>Substantial attraction to more than one binary sex category.</summary>
        Bisexual,

        /// <summary>Low or absent sexual attraction.</summary>
        Asexual
    }
}
