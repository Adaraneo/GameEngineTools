// IPsychologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using GameEngineTools.Characters.Core;

    public interface IPsychologyEngineFactory
    {
        IPsychologyEngine Create(IRandomSource rng);
    }
}
