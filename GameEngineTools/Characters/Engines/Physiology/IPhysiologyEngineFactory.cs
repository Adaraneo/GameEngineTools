// IPhysiologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public interface IPhysiologyEngineFactory
    {
        IPhysiologyEngine Create(IRandomSource rng, SexBiology biology, WDateOnly birthDate ,WDateOnly now);
    }
}
