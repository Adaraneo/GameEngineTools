// IPsychologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using GameEngineTools.Characters.Core;

    /// <summary>Creates <see cref="IPsychologyEngine"/> instances seeded for a specific character.</summary>
    public interface IPsychologyEngineFactory
    {
        /// <summary>Creates a psychology engine for a character.</summary>
        /// <param name="rng">Deterministic random source.</param>
        IPsychologyEngine Create(IRandomSource rng);
    }
}
