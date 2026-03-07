// DefaultPhysiologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class PhysiologyEngineFactory<TImpl> : IPhysiologyEngineFactory where TImpl : class, IPhysiologyEngine
    {
        private readonly IServiceProvider _sp;

        public PhysiologyEngineFactory(IServiceProvider sp) => _sp = sp;

        public IPhysiologyEngine Create(IRandomSource rng, SexBiology biology) => ActivatorUtilities.CreateInstance<TImpl>(_sp, rng, biology);
    }
}
