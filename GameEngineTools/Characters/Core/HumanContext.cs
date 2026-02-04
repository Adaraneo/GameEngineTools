// HumanContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public sealed class HumanContext : IHumanContext
    {
        public HumanId Id { get; init; }
        public Identity Identity { get; init; } = default!;
        public SexBiology Biology { get; init; }
        public Traits.Personality Personality { get; init; } = default!;
        public EnginesSnapshot Snapshot { get; internal set; } = default!;
        public IEventBus EventBus { get; init; } = default!;
        public IScheduler Scheduler { get; init; } = default!;
        public IRandomSource Random { get; init; } = default!;
        public ILogger Logger { get; init; } = default!;
    }
}
