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
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal class DefaultPsychologyEngineFactory : IPsychologyEngineFactory
    {
        private readonly IOptions<PsychologyConfig> _cfg;
        private readonly ILoggerFactory _loggerFactory;

        public DefaultPsychologyEngineFactory(IOptions<PsychologyConfig> cfg, ILoggerFactory loggerFactory)
        {
            _cfg = cfg;
            _loggerFactory = loggerFactory;
        }
        public IPsychologyEngine Create(IRandomSource rng) => new DefaultPsychologyEngine(_cfg, _loggerFactory, rng);
    }
}
