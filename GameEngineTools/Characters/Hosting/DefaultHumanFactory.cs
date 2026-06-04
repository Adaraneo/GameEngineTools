// DefaultHumanFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Interests;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Engines.SelfConcept;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Engines.Values;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
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

        /// <summary>
        /// Reconstructs a character from a persisted blueprint and snapshot,
        /// revalidating age-dependent state against the current simulation time.
        /// </summary>
        IHuman Load(HumanBlueprint blueprint, EnginesSnapshot snapshot);
    }

    /// <summary>
    /// Immutable description of a character used as input to <see cref="IHumanFactory"/>.
    /// Generated once per character and persisted alongside <see cref="CharacterData"/>.
    /// </summary>
    /// <param name="Id">Unique character identifier.</param>
    /// <param name="Identity">Name and birth date.</param>
    /// <param name="Biology">Biological sex.</param>
    /// <param name="Personality">Big Five + motivation weights.</param>
    /// <param name="GeneticBlueprint">Immutable genetic traits — age effects are projected at runtime by <see cref="AppearanceProjector"/>.</param>
    /// <param name="AttractionProfile">
    /// Personal physical-preference profile used by <c>IAttractionCalculator</c>.
    /// Nullable for backwards compatibility with characters created before this field existed.
    /// </param>
    /// <param name="Seed">Optional RNG seed for deterministic engine initialisation.</param>
    /// <param name="Occupation">
    /// The character's occupation ID (see <c>OccupationIds</c>), used to seed the daily schedule.
    /// <c>null</c> or empty string means no fixed routine.
    /// </param>
    public sealed record HumanBlueprint(
        HumanId Id,
        Identity Identity,
        SexBiology Biology,
        Personality Personality,
        GeneticBlueprint GeneticBlueprint,
        AttractionProfile? AttractionProfile = null,
        int? Seed = null,
        string? Occupation = null,
        /// <summary>
        /// Pre-generated Schwartz values profile for this character.
        /// When <c>null</c>, <see cref="DefaultHumanFactory"/> generates one from <see cref="Personality"/>.
        /// Nullable to support characters loaded from saves created before this sprint.
        /// </summary>
        GameEngineTools.Characters.Traits.ValuesProfile? ValuesProfile = null);

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
            var schedule = _sp.GetRequiredService<IDailyScheduleEngine>();
            var valuesEngine = _sp.GetRequiredService<IValuesEngine>();
            var selfConcept = _sp.GetRequiredService<ISelfConceptEngine>();
            var interestEngine = _sp.GetRequiredService<IInterestEngine>();

            // Object interaction engine is optional — only wired when both the world object provider
            // and location service are registered in the DI container.
            var objectInteraction = _sp.GetService<IObjectInteractionEngine>();

            // Generate values profile from BigFive (Parks-Leduc et al. 2015 meta-analysis coefficients).
            // Use the blueprint's pre-generated profile if provided (deterministic replays / saves).
            // The generated profile is the immutable Baseline; Current starts identical and drifts (R4 drift).
            var rngForValues = new System.Random(DeriveSeed(b.Id) ^ 0x56A1_CCFF);
            var valuesBaseline = b.ValuesProfile ?? ValuesProfileGenerator.Generate(b.Personality.BigFive, rngForValues);
            var valuesState = ValuesState.FromBaseline(valuesBaseline);

            // Generate RIASEC interest baseline (BigFive + sex + occupational exposure; Larson 2002, Su 2009).
            var rngForInterests = new System.Random(DeriveSeed(b.Id) ^ 0x1A7E_5E57);
            var interestBaseline = InterestProfileGenerator.Generate(
                b.Personality.BigFive, b.Biology, b.Occupation, rngForInterests);
            var interestState = InterestState.FromBaseline(interestBaseline);

            _loggerFactory.CreateLogger<DefaultHumanFactory>()
                .ValuesProfileGenerated(
                    b.Id.Value.ToString(),
                    valuesBaseline.Benevolence,
                    valuesBaseline.Universalism,
                    valuesBaseline.Achievement,
                    valuesBaseline.Power);

            // Initial snapshot — State is always valid immediately after factory creation
            var snapshot = new EnginesSnapshot(
                physio.State, psych.State, behav.State, inter.State, rel.State, mem.State, semantic.State,
                Goals: goal.State, Schedule: schedule.State, Values: valuesState,
                SelfConcept: selfConcept.State, Interests: interestState);

            var human = new OrchestratedHuman(
                b.Id, b.Identity, b.Biology, b.Personality, b.GeneticBlueprint,
                b.AttractionProfile,
                bus, scheduler, rng, logger,
                physio, psych, behav, inter, rel, mem, semantic, goal, schedule, valuesEngine, selfConcept,
                interestEngine,
                snapshot,
                _behaviorCadencePolicy,
                objectInteraction);

            goal.SeedFromPersonality(b.Personality, _clock.Now > b.Identity.BirthDate.ToDateTime() ? b.Identity.BirthDate.ToDateTime() : _clock.Now);

            schedule.SeedFromOccupation(b.Occupation, b.Personality, _clock.Now > b.Identity.BirthDate.ToDateTime() ? b.Identity.BirthDate.ToDateTime() : _clock.Now, scheduler, b.Id);

            valuesEngine.SeedFromBaseline(valuesBaseline);

            selfConcept.SeedFromPersonality(b.Personality);

            interestEngine.SeedFromBaseline(interestBaseline);

            // Propagate seeded states into the externally visible snapshot so that
            // code reading human.Snapshot before the first Tick() sees the correct state,
            // and persistence snapshots include goals, schedule, values, and self-concept from creation.
            human.RestoreSnapshot(human.Snapshot with
            {
                Goals       = goal.State,
                Schedule    = schedule.State,
                Values      = valuesState,
                SelfConcept = selfConcept.State,
                Interests   = interestState
            }, _clock.Now.Date);

            return human;
        }

        public IHuman Load(HumanBlueprint blueprint, EnginesSnapshot snapshot)
        {
            var human = (OrchestratedHuman)Create(blueprint);

            // Restore persisted snapshot with current game time so age-dependent
            // subsystems (cycle, testosterone) are revalidated at load time.
            human.RestoreSnapshot(snapshot, _clock.Now.Date);

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
