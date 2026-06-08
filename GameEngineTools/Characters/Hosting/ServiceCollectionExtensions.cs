// ServiceCollectionExtensions.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Attraction;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Goals;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Interests;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Objects;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.Reputation;
using GameEngineTools.Characters.Engines.Schedule;
using GameEngineTools.Characters.Engines.SelfConcept;
using GameEngineTools.Characters.Engines.SemanticMemory;
using GameEngineTools.Characters.Engines.Sleep;
using GameEngineTools.Characters.Engines.Values;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Characters.Generation.Portraits;
using GameEngineTools.Characters.Hosting.Defaults;
using GameEngineTools.Constants;
using GameEngineTools.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Extension metody pro <see cref="IServiceCollection"/> — registrace herních systémů do DI.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        #region Core

        /// <summary>
        /// Zaregistruje jádro (EventBus, Scheduler, RNG factory, HumanFactory).
        /// Defaulty jsou "in-memory" a dají se přepsat vlastními implementacemi dřív nebo
        /// později v pipeline přes <c>TryAdd*</c> sémantiku.
        /// </summary>
        public static IServiceCollection AddCharactersCore(this IServiceCollection services)
        {
            services.TryAddTransient<IEventBus, InMemoryEventBus>();
            services.TryAddTransient<IScheduler, SimpleScheduler>();
            services.TryAddSingleton<IRandomSourceFactory, RandomSourceFactory>();
            services.TryAddSingleton<ICognitiveResolutionLevelRuntime, DefaultCognitiveResolutionLevelRuntime>();
            services.TryAddSingleton<ICharacterDevelopmentPolicy, DefaultCharacterDevelopmentPolicy>();
            services.TryAddSingleton<IBehaviorCadencePolicy, DefaultBehaviorCadencePolicy>();
            services.TryAddSingleton<IMemoryFidelityPolicy, DefaultMemoryFidelityPolicy>();
            services.TryAddSingleton<IPerceptionFidelityPolicy, DefaultPerceptionFidelityPolicy>();
            services.TryAddSingleton<ISocialFidelityPolicy, DefaultSocialFidelityPolicy>();
            services.TryAddSingleton<IHumanFactory, DefaultHumanFactory>();
            services.TryAddTransient<IGoalEngine, DefaultGoalEngine>();
            services.TryAddSingleton<IOccupationRegistry>(_ =>
            {
                var registry = new DefaultOccupationRegistry();
                BuiltInOccupationRegistrar.RegisterAll(registry);

                // Load custom occupations from SourceFiles\Characters\Occupations.csv if present.
                // Built-in occupations are always registered above — this file only adds extras.
                var csvPath = Constants.FileSystemConstant.SourceFilePath.Occupations;
                if (File.Exists(csvPath))
                    OccupationDefinitionLoader.LoadFromCsv(csvPath, registry);

                return registry;
            });
            services.TryAddTransient<IDailyScheduleEngine, DefaultDailyScheduleEngine>();
            services.TryAddTransient<IValuesEngine, DefaultValuesEngine>();
            services.TryAddTransient<ISelfConceptEngine, DefaultSelfConceptEngine>();
            services.TryAddTransient<IInterestEngine, DefaultInterestEngine>();

            // Scene-level reputation aggregate (singleton — shared across all characters in a world).
            services.TryAddSingleton<CommunityReputationLedger>();

            var valuesOb = services.AddOptions<ValuesConfig>();
            valuesOb.BindConfiguration("Characters:Values");

            var selfConceptOb = services.AddOptions<SelfConceptConfig>();
            selfConceptOb.BindConfiguration("Characters:SelfConcept");

            var interestsOb = services.AddOptions<InterestConfig>();
            interestsOb.BindConfiguration("Characters:Interests");

            var lodOb = services.AddOptions<CognitiveResolutionLevelConfig>();
            lodOb.BindConfiguration("Characters:Lod");

            var fidelityOb = services.AddOptions<CharacterFidelityConfig>();
            fidelityOb.BindConfiguration("Characters:Fidelity");

            return services;
        }

        #endregion Core

        #region CharacterGeneration

        /// <summary>
        /// Zaregistruje generátor postav s předem vytvořeným <see cref="HumanBlueprintSpec"/>.
        /// </summary>
        /// <param name="services">DI kolekce.</param>
        /// <param name="humanBlueprintSpec">Specifikace blueprintu — váhy pohlaví, výchozí rozsah věku.</param>
        /// <remarks>
        /// Pokud potřebuješ spec sestavit až po startu DI (např. z <c>WorldTimeContext</c>),
        /// použij overload <see cref="AddCharacterGeneration(IServiceCollection, Func{IServiceProvider, HumanBlueprintSpec})"/>.
        /// </remarks>
        public static IServiceCollection AddCharacterGeneration(
            this IServiceCollection services,
            HumanBlueprintSpec humanBlueprintSpec)
        {
            services.AddSingleton(humanBlueprintSpec);
            return services.AddCharacterGenerationCore();
        }

        /// <summary>
        /// Zaregistruje generátor postav s lazy factory pro <see cref="HumanBlueprintSpec"/>.
        /// Factory je vyhodnocena až při prvním resolve — v té době je DI kontejner plně sestaven,
        /// takže může záviset na libovolném singletons (typicky <c>WorldTimeContext</c>).
        /// </summary>
        /// <param name="services">DI kolekce.</param>
        /// <param name="specFactory">
        /// Factory funkce pro sestavení <see cref="HumanBlueprintSpec"/> z DI provideru.
        /// </param>
        /// <example>
        /// <code>
        /// s.AddCharacterGeneration(sp =>
        /// {
        ///     var ctx = sp.GetRequiredService&lt;WorldTimeContext&gt;();
        ///     return HumanBlueprintSpec.Default(ctx.GetDate(ctx.Now()), ctx);
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddCharacterGeneration(
            this IServiceCollection services,
            Func<IServiceProvider, HumanBlueprintSpec> specFactory)
        {
            services.AddSingleton(specFactory);
            return services.AddCharacterGenerationCore();
        }

        /// <summary>
        /// Registers the family generation and topology services.
        /// </summary>
        /// <remarks>
        /// Call after <c>AddCharacterGeneration()</c> — NuclearFamilyGenerator depends
        /// on IHumanBlueprintGenerator, IChildBlueprintGenerator, and IHumanFactory
        /// which are registered there.
        /// </remarks>
        public static IServiceCollection AddFamilySystem(this IServiceCollection services)
        {
            // Central family registry — one instance per scene lifetime.
            // Holds surname → members and character → kin links indexes.
            services.AddSingleton<FamilyGraph>();

            // Orchestrates parent generation, genetic child inheritance,
            // and FamilyBuilder edge seeding in a single call.
            services.AddSingleton<NuclearFamilyGenerator>();

            return services;
        }

        /// <summary>
        /// Sdílená registrace generátorů — volaná oběma overloady <c>AddCharacterGeneration</c>.
        /// </summary>
        private static IServiceCollection AddCharacterGenerationCore(this IServiceCollection services)
        {
            services.AddSingleton<IHumanBlueprintGenerator, HumanBlueprintGenerator>();
            services.AddSingleton<IChildBlueprintGenerator, ChildBlueprintGenerator>();
            services.AddSingleton<IIdentityGenerator>(_ =>
            {
                var femaleNames = CsvLoader.Load(FileSystemConstant.SourceFilePath.FemaleNames,
                    v => new Name { Original = v[0], Familiar = v[1].Split(' ') });
                var maleNames = CsvLoader.Load(FileSystemConstant.SourceFilePath.MaleNames,
                    v => new Name { Original = v[0], Familiar = v[1].Split(' ') });
                var surnames = CsvLoader.Load(FileSystemConstant.SourceFilePath.Surnames,
                    v => new Surname { Male = v[0], Female = v[1] });

                return new SimpleIdentityGenerator(
                    femaleNames.ToArray(),
                    maleNames.ToArray(),
                    surnames.ToArray());
            });
            services.AddAppearanceGenerator();
            services.AddSingleton<IAttractionProfileGenerator, AttractionProfileGenerator>();
            services.AddSingleton<IAttractionCalculator, DefaultAttractionCalculator>();
            services.AddPersonalityGenerator();
            return services;
        }

        private static IServiceCollection AddPersonalityGenerator(this IServiceCollection services)
            => services.AddSingleton<IPersonalityGenerator, PersonalityGenerator>();

        private static IServiceCollection AddAppearanceGenerator(this IServiceCollection services)
        {
            services.AddSingleton<IAppearanceGenerator, AppearanceGenerator>();
            services.AddSingleton<IPortraitSpecBuilder, PortraitSpecBuilder>();
            services.AddSingleton<IPortraitPromptFormatter, PortraitPromptFormatter>();
            return services;
        }

        #endregion CharacterGeneration

        #region Engine registrace

        /// <summary>Registrace implementace Physiology engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddPhysiologyEngine<TImpl>(
            this IServiceCollection services,
            Action<PhysiologyConfig>? configure = null)
            where TImpl : class, IPhysiologyEngine
        {
            services.AddSingleton<IPhysiologyEngineFactory, PhysiologyEngineFactory<TImpl>>();
            var ob = services.AddOptions<PhysiologyConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:Physiology");
            }

            return services;
        }

        /// <summary>Registrace implementace Psychology engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddPsychologyEngine<TImpl>(
            this IServiceCollection services,
            Action<PsychologyConfig>? configure = null)
            where TImpl : class, IPsychologyEngine
        {
            services.AddSingleton<IPsychologyEngineFactory, PsychologyEngineFactory<TImpl>>();
            var ob = services.AddOptions<PsychologyConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:Psychology");
            }

            return services;
        }

        /// <summary>
        /// Registrace implementace Behavior engine + jeho konfigurace přes Options.
        /// Zároveň registruje <see cref="SleepConfig"/>, která je potřebná pro
        /// <see cref="DefaultBehaviorEngine"/> a <see cref="DefaultSleepSession"/>.
        /// </summary>
        public static IServiceCollection AddBehaviorEngine<TImpl>(
            this IServiceCollection services,
            Action<BehaviorConfig>? configure = null,
            Action<SleepConfig>? sleepConfigure = null)
            where TImpl : class, IBehaviorEngine
        {
            services.AddTransient<IBehaviorEngine, TImpl>();

            var behavOb = services.AddOptions<BehaviorConfig>();
            if (configure != null)
                behavOb.Configure(configure);
            else
                behavOb.BindConfiguration("Characters:Behavior");

            var sleepOb = services.AddOptions<SleepConfig>();
            if (sleepConfigure != null)
                sleepOb.Configure(sleepConfigure);
            else
                sleepOb.BindConfiguration("Characters:Sleep");

            return services;
        }

        /// <summary>Registrace implementace Interactions engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddInteractionEngine<TImpl>(
            this IServiceCollection services,
            Action<InteractionConfig>? configure = null)
            where TImpl : class, IInteractionEngine
        {
            services.AddTransient<IInteractionEngine, TImpl>();
            var ob = services.AddOptions<InteractionConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:Interactions");
            }

            return services;
        }

        /// <summary>Registrace implementace Relationships engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddRelationshipsEngine<TImpl>(
            this IServiceCollection services,
            Action<RelationshipsConfig>? configure = null)
            where TImpl : class, IRelationshipsEngine
        {
            services.AddTransient<IRelationshipsEngine, TImpl>();
            var ob = services.AddOptions<RelationshipsConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:Relationships");
            }

            return services;
        }

        /// <summary>Registrace implementace Memory engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddMemoryEngine<TImpl>(
            this IServiceCollection services,
            Action<MemoryConfig>? configure = null)
            where TImpl : class, IMemoryEngine
        {
            services.AddTransient<IMemoryEngine, TImpl>();
            var ob = services.AddOptions<MemoryConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:Memory");
            }

            return services;
        }

        /// <summary>Registrace implementace SemanticMemory engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddSemanticMemoryEngine<TImpl>(
            this IServiceCollection services,
            Action<SemanticMemoryConfig>? configure = null)
            where TImpl : class, ISemanticMemoryEngine
        {
            services.AddTransient<ISemanticMemoryEngine, TImpl>();
            var ob = services.AddOptions<SemanticMemoryConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:SemanticMemory");
            }

            return services;
        }

        /// <summary>Registrace implementace DailySchedule engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddDailyScheduleEngine<TImpl>(
            this IServiceCollection services,
            Action<DailyScheduleConfig>? configure = null)
            where TImpl : class, IDailyScheduleEngine
        {
            services.AddTransient<IDailyScheduleEngine, TImpl>();
            var ob = services.AddOptions<DailyScheduleConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:DailySchedule");
            }

            return services;
        }

        /// <summary>Registrace implementace Goal engine + jeho konfigurace přes Options.</summary>
        public static IServiceCollection AddGoalEngine<TImpl>(
            this IServiceCollection services,
            Action<GoalConfig>? configure = null)
            where TImpl : class, IGoalEngine
        {
            services.AddTransient<IGoalEngine, TImpl>();
            var ob = services.AddOptions<GoalConfig>();
            if (configure != null)
            {
                ob.Configure(configure);
            }
            else
            {
                ob.BindConfiguration("Characters:Goals");
            }

            return services;
        }

        #endregion Engine registrace

        #region Object Interaction engine

        /// <summary>
        /// Registers the object interaction subsystem: policy and engine.
        /// Requires a world-object provider and
        /// <see cref="GameEngineTools.World.Location.ILocationService"/> to also be registered.
        /// </summary>
        public static IServiceCollection AddObjectInteractionEngine(this IServiceCollection services)
        {
            services.TryAddSingleton<IObjectInteractionPolicy, DefaultObjectInteractionPolicy>();
            services.TryAddSingleton<IObjectInteractionEngine, DefaultObjectInteractionEngine>();
            return services;
        }

        #endregion Object Interaction engine

        #region Zkrácená registrace všeho najednou

        /// <summary>
        /// Zkrácená registrace všech enginů najednou.
        /// </summary>
        public static IServiceCollection AddCharacters<TPhysio, TPsych, TBehav, TInter, TRel, TMem, TSem, TGoal, TSchedule>(
            this IServiceCollection services,
            Action<PhysiologyConfig>? physio = null,
            Action<PsychologyConfig>? psych = null,
            Action<BehaviorConfig>? behav = null,
            Action<SleepConfig>? sleep = null,
            Action<InteractionConfig>? inter = null,
            Action<RelationshipsConfig>? rel = null,
            Action<MemoryConfig>? mem = null,
            Action<SemanticMemoryConfig>? semantic = null,
            Action<GoalConfig>? goal = null,
            Action<DailyScheduleConfig>? schedule = null)
            where TPhysio : class, IPhysiologyEngine
            where TPsych : class, IPsychologyEngine
            where TBehav : class, IBehaviorEngine
            where TInter : class, IInteractionEngine
            where TRel : class, IRelationshipsEngine
            where TMem : class, IMemoryEngine
            where TSem : class, ISemanticMemoryEngine
            where TGoal : class, IGoalEngine
            where TSchedule : class, IDailyScheduleEngine
        {
            services.AddCharactersCore()
                    .AddPhysiologyEngine<TPhysio>(physio)
                    .AddPsychologyEngine<TPsych>(psych)
                    .AddBehaviorEngine<TBehav>(behav, sleep)
                    .AddInteractionEngine<TInter>(inter)
                    .AddRelationshipsEngine<TRel>(rel)
                    .AddMemoryEngine<TMem>(mem)
                    .AddSemanticMemoryEngine<TSem>(semantic)
                    .AddGoalEngine<TGoal>(goal)
                    .AddDailyScheduleEngine<TSchedule>(schedule);

            return services;
        }

        #endregion Zkrácená registrace všeho najednou
    }
}
