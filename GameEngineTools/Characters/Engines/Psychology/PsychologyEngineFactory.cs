// PsychologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Psychology
{
    using System;
    using GameEngineTools.Characters.Core;
    using Microsoft.Extensions.DependencyInjection;

    internal sealed class PsychologyEngineFactory<TImpl> : IPsychologyEngineFactory where TImpl : class, IPsychologyEngine
    {
        private readonly IServiceProvider _sp;

        public PsychologyEngineFactory(IServiceProvider sp) => _sp = sp;

        public IPsychologyEngine Create(IRandomSource rng) => ActivatorUtilities.CreateInstance<TImpl>(_sp, rng);
    }
}
