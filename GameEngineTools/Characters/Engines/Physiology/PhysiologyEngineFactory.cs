// PhysiologyEngineFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Physiology
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;

    internal sealed class PhysiologyEngineFactory<TImpl> : IPhysiologyEngineFactory where TImpl : class, IPhysiologyEngine
    {
        private readonly IServiceProvider _sp;

        public PhysiologyEngineFactory(IServiceProvider sp) => _sp = sp;

        public IPhysiologyEngine Create(IRandomSource rng, SexBiology biology, WDateOnly now) => ActivatorUtilities.CreateInstance<TImpl>(_sp, rng, biology, now);
    }
}
