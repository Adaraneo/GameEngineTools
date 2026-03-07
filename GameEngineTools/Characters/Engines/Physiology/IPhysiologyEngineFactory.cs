// IPhysiologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;

    public interface IPhysiologyEngineFactory
    {
        IPhysiologyEngine Create(IRandomSource rng, SexBiology biology);
    }
}
