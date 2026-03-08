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
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameEngineTools.Characters.Hosting
{

    /// <summary>
    /// Továrna vytváří OrchestratedHuman a per-postava instance služeb.
    /// Enginy jsou vytvářeny přes jejich vlastní factory — runtime parametry (rng, biology)
    /// jsou tak předány čistě přes konstruktor, bez nutnosti mít IHumanContext v DI kontejneru.
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
        PhysicalAppearance PhysicalAppearance,
        int? Seed = null);

    public sealed class DefaultHumanFactory : IHumanFactory
    {
        private readonly IServiceProvider _sp;
        private readonly IRandomSourceFactory _rngFactory;
        private readonly ILoggerFactory _loggerFactory;

        private readonly IPhysiologyEngineFactory _physioFactory;
        private readonly IPsychologyEngineFactory _psychFactory;
        private readonly IClock _clock;
        private readonly WorldTimeContext _wtctx;

        public DefaultHumanFactory(IServiceProvider sp, IRandomSourceFactory rngFactory, ILoggerFactory loggerFactory, IPhysiologyEngineFactory physioFactory, IPsychologyEngineFactory psychFactory, IClock clock, WorldTimeContext wtctx)
        {
            _sp = sp;
            _rngFactory = rngFactory;
            _loggerFactory = loggerFactory;
            _physioFactory = physioFactory;
            _psychFactory = psychFactory;
            _clock = clock;
            _wtctx = wtctx;
        }

        public IHuman Create(HumanBlueprint b)
        {
            // Per-postava služby (transient → nové instance)
            var bus = _sp.GetRequiredService<IEventBus>();
            var scheduler = _sp.GetRequiredService<IScheduler>();
            var rng = _rngFactory.Create(b.Seed ?? DeriveSeed(b.Id));
            var logger = _loggerFactory.CreateLogger($"Characters.Human[{b.Id.Value}]");

            // Engines vytvořené přes factories
            var physio = _physioFactory.Create(rng, b.Biology, _wtctx.GetDate(_clock.Now));
            var psych = _psychFactory.Create(rng);

            // Engines bez runtime parametrů - stačí DI
            var behav = _sp.GetRequiredService<IBehaviorEngine>();
            var inter = _sp.GetRequiredService<IInteractionEngine>();
            var rel = _sp.GetRequiredService<IRelationshipsEngine>();
            var mem = _sp.GetRequiredService<IMemoryEngine>();

            // Počáteční snapshot - State je vždy platný, factory to zaručuje
            var snapshot = new EnginesSnapshot(
                physio.State, psych.State, behav.State, inter.State, rel.State, mem.State);

            var human = new OrchestratedHuman(
                b.Id, b.Identity, b.Biology, b.Personality, b.PhysicalAppearance,
                bus, scheduler, rng, logger,
                physio, psych, behav, inter, rel, mem,
                snapshot);

            bus.SubscribeAll(human.ReceiveEvent);

            return human;
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
