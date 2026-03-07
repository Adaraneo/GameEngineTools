// DefaultPsychologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
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

    internal sealed class PsychologyEngineFactory<TImpl> : IPsychologyEngineFactory where TImpl : class, IPsychologyEngine
    {
        private readonly IServiceProvider _sp;

        public PsychologyEngineFactory(IServiceProvider sp) => _sp = sp;

        public IPsychologyEngine Create(IRandomSource rng) => ActivatorUtilities.CreateInstance<TImpl>(_sp, rng);
    }
}
