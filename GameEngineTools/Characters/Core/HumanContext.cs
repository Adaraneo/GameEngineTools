// HumanContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using Microsoft.Extensions.Logging;

    public sealed class HumanContext : IHumanContext
    {
        public HumanId Id { get; init; }
        public Identity Identity { get; init; } = default!;
        public SexBiology Biology { get; init; }
        public Traits.Personality Personality { get; init; } = default!;
        public Traits.PsychologicalProfile PsychologyProfile { get; init; } = Traits.PsychologicalProfile.Default;
        public Traits.AttractionProfile? AttractionProfile { get; init; }
        public EnginesSnapshot Snapshot { get; internal set; } = default!;
        public IEventBus EventBus { get; init; } = default!;
        public IScheduler Scheduler { get; init; } = default!;
        public IRandomSource Random { get; init; } = default!;
        public ILogger Logger { get; init; } = default!;
    }
}
