// IPhysiologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>Creates <see cref="IPhysiologyEngine"/> instances seeded for a specific character.</summary>
    public interface IPhysiologyEngineFactory
    {
        /// <summary>Creates a physiology engine for a character.</summary>
        /// <param name="rng">Deterministic random source.</param>
        /// <param name="biology">Biological sex.</param>
        /// <param name="birthDate">Character birth date.</param>
        /// <param name="now">Current game date.</param>
        IPhysiologyEngine Create(IRandomSource rng, SexBiology biology, WDateOnly birthDate, WDateOnly now);
    }
}
