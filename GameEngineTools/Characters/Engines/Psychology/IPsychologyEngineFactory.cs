// IPsychologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;

    public interface IPsychologyEngineFactory
    {
        IPsychologyEngine Create(IRandomSource rng);
    }
}
