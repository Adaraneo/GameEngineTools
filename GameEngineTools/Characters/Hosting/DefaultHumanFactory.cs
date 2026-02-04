// DefaultHumanFactory.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Characters.Hosting.Defaults;
using GameEngineTools.Characters.Traits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameEngineTools.Characters.Hosting
{

    /// <summary>
    /// Továrna vytváří OrchestratedHuman a per-postava instance služeb.
    /// Enginy jsou resolvované jako Transient – jejich životnost sváže objekt postavy.
    /// </summary>
    public interface IHumanFactory
    {
        IHuman Create(HumanBlueprint blueprint);
    }

    public sealed record HumanBlueprint(
        HumanId Id,
        Identity Identity,
        SexBiology Biology,
        Personality Personality,
        int? Seed = null);

    public sealed class DefaultHumanFactory : IHumanFactory
    {
        private readonly IServiceProvider _sp;
        private readonly IRandomSourceFactory _rngFactory;
        private readonly ILoggerFactory _loggerFactory;

        public DefaultHumanFactory(IServiceProvider sp, IRandomSourceFactory rngFactory, ILoggerFactory loggerFactory)
        {
            _sp = sp;
            _rngFactory = rngFactory;
            _loggerFactory = loggerFactory;
        }

        public IHuman Create(HumanBlueprint b)
        {
            // Per-postava služby (transient → nové instance)
            var bus = _sp.GetRequiredService<IEventBus>();
            var scheduler = _sp.GetRequiredService<IScheduler>();
            var rng = _rngFactory.Create(b.Seed ?? DeriveSeed(b.Id));
            var logger = _loggerFactory.CreateLogger($"Characters.Human[{b.Id.Value}]");

            // Enginy (transient)
            var physio = _sp.GetRequiredService<IPhysiologyEngine>();
            var psych = _sp.GetRequiredService<IPsychologyEngine>();
            var behav = _sp.GetRequiredService<IBehaviorEngine>();
            var inter = _sp.GetRequiredService<IInteractionEngine>();
            var rel = _sp.GetRequiredService<IRelationshipsEngine>();
            var mem = _sp.GetRequiredService<IMemoryEngine>();

            // Počáteční snapshot složený z výchozích stavů engine (před prvním Tickem)
            var snapshot = new EnginesSnapshot(
                physio.State, psych.State, behav.State, inter.State, rel.State, mem.State);

            return new OrchestratedHuman(
                b.Id, b.Identity, b.Biology, b.Personality,
                bus, scheduler, rng, logger,
                physio, psych, behav, inter, rel, mem,
                snapshot);
        }

        private static int DeriveSeed(HumanId id)
        {
            // Deterministický seed z Guidu (stabilní, ale rozumně rozptýlený)
            var bytes = id.Value.ToByteArray();
            unchecked
            {
                int hash = 17;
                foreach (var bt in bytes) hash = hash * 31 + bt;
                return hash;
            }
        }
    }
}
