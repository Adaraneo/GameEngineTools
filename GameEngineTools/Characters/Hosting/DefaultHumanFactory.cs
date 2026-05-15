// DefaultHumanFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Contract for the factory that constructs a fully wired <see cref="IHuman"/> from a blueprint.
    /// </summary>
    public interface IHumanFactory
    {
        /// <summary>
        /// Creates a new <see cref="IHuman"/> instance from the given blueprint.
        /// All engine instances are created fresh — no shared state between characters.
        /// </summary>
        /// <param name="blueprint">The blueprint that describes the character.</param>
        /// <returns>A fully initialised <see cref="IHuman"/> ready for simulation.</returns>
        IHuman Create(HumanBlueprint blueprint);
    }

    /// <summary>
    /// Immutable description of a character used as input to <see cref="IHumanFactory"/>.
    /// Generated once per character and persisted alongside <see cref="CharacterData"/>.
    /// </summary>
    /// <param name="Id">Unique character identifier.</param>
    /// <param name="Identity">Name and birth date.</param>
    /// <param name="Biology">Biological sex.</param>
    /// <param name="Personality">Big Five + motivation weights.</param>
    /// <param name="PhysicalAppearance">Stable morphological traits (height, frame, colouring…).</param>
    /// <param name="AttractionProfile">
    /// Personal physical-preference profile used by <c>IAttractionCalculator</c>.
    /// Nullable for backwards compatibility with characters created before this field existed.
    /// </param>
    /// <param name="Seed">Optional RNG seed for deterministic engine initialisation.</param>
    public sealed record HumanBlueprint(
        HumanId Id,
        Identity Identity,
        SexBiology Biology,
        Personality Personality,
        PhysicalAppearance PhysicalAppearance,
        AttractionProfile? AttractionProfile = null,
        int? Seed = null);

    /// <summary>
    /// Default implementation of <see cref="IHumanFactory"/>.
    /// Creates per-character engine instances and wires them into an <see cref="OrchestratedHuman"/>.
    /// </summary>
    /// <remarks>
    /// Engines are created via dedicated factories (<see cref="IPhysiologyEngineFactory"/>,
    /// <see cref="IPsychologyEngineFactory"/>) so that runtime parameters (RNG, biology, birth date)
    /// are passed through constructors without polluting the DI container with per-character state.
    /// </remarks>
    public sealed class DefaultHumanFactory : IHumanFactory
    {
        #region Private fields

        private readonly IServiceProvider _sp;
        private readonly IRandomSourceFactory _rngFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IPhysiologyEngineFactory _physioFactory;
        private readonly IPsychologyEngineFactory _psychFactory;
        private readonly IClock _clock;
        private readonly IBehaviorCadencePolicy _behaviorCadencePolicy;

        #endregion Private fields

        #region Constructor

        /// <summary>
        /// Initialises the factory with all required dependencies.
        /// </summary>
        public DefaultHumanFactory(
            IServiceProvider sp,
            IRandomSourceFactory rngFactory,
            ILoggerFactory loggerFactory,
            IPhysiologyEngineFactory physioFactory,
            IPsychologyEngineFactory psychFactory,
            IClock clock,
            IBehaviorCadencePolicy behaviorCadencePolicy)
        {
            _sp = sp;
            _rngFactory = rngFactory;
            _loggerFactory = loggerFactory;
            _physioFactory = physioFactory;
            _psychFactory = psychFactory;
            _clock = clock;
            _behaviorCadencePolicy = behaviorCadencePolicy;
        }

        #endregion Constructor

        #region IHumanFactory

        /// <inheritdoc/>
        public IHuman Create(HumanBlueprint b)
        {
            // Per-character services (transient → new instance each time)
            var bus = _sp.GetRequiredService<IEventBus>();
            var scheduler = _sp.GetRequiredService<IScheduler>();
            var rng = _rngFactory.Create(b.Seed ?? DeriveSeed(b.Id));
            var logger = _loggerFactory.CreateLogger($"Characters.Human[{b.Id.Value}]");

            // Engines created via factories (require runtime parameters)
            var physio = _physioFactory.Create(rng, b.Biology, b.Identity.BirthDate, _clock.Now.Date);
            var psych = _psychFactory.Create(rng);

            // Engines without runtime parameters — resolved directly from DI
            var behav = _sp.GetRequiredService<IBehaviorEngine>();
            var inter = _sp.GetRequiredService<IInteractionEngine>();
            var rel = _sp.GetRequiredService<IRelationshipsEngine>();
            var mem = _sp.GetRequiredService<IMemoryEngine>();
            var semantic = _sp.GetRequiredService<ISemanticMemoryEngine>();
            var goal = _sp.GetRequiredService<IGoalEngine>();

            // Initial snapshot — State is always valid immediately after factory creation
            var snapshot = new EnginesSnapshot(
                physio.State, psych.State, behav.State, inter.State, rel.State, mem.State, semantic.State,
                Goals: goal.State);

            var human = new OrchestratedHuman(
                b.Id, b.Identity, b.Biology, b.Personality, b.PhysicalAppearance,
                b.AttractionProfile,
                bus, scheduler, rng, logger,
                physio, psych, behav, inter, rel, mem, semantic, goal,
                snapshot,
                _behaviorCadencePolicy);

            goal.SeedFromPersonality(b.Personality, _clock.Now);
            return human;
        }

        #endregion IHumanFactory

        #region Private helpers

        /// <summary>
        /// Derives a deterministic integer seed from a <see cref="HumanId"/> GUID.
        /// Stable across runs, sufficiently spread to avoid clustering.
        /// </summary>
        private static int DeriveSeed(HumanId id)
        {
            var bytes = id.Value.ToByteArray();
            unchecked
            {
                var hash = 17;
                foreach (var bt in bytes)
                {
                    hash = hash * 31 + bt;
                }

                return hash;
            }
        }

        #endregion Private helpers
    }
}
