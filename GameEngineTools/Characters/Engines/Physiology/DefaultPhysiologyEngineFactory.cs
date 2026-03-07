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

    internal sealed class DefaultPhysiologyEngineFactory : IPhysiologyEngineFactory
    {
        private readonly IOptions<PhysiologyConfig> _cfg;
        private readonly IOptions<MenstrualCycleConfig> _cycleCfg;
        private readonly ILoggerFactory _loggerFactory;

        public DefaultPhysiologyEngineFactory(IOptions<PhysiologyConfig> cfg, IOptions<MenstrualCycleConfig> cycleCfg, ILoggerFactory loggerFactory)
        {
            _cfg = cfg;
            _cycleCfg = cycleCfg;
            _loggerFactory = loggerFactory;
        }
        public IPhysiologyEngine Create(IRandomSource rng, SexBiology biology) => new DefaultPhysiologyEngine(_cfg, _cycleCfg, _loggerFactory, rng, biology);
    }
}
