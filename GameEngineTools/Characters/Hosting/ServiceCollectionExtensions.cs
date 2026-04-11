// ServiceCollectionExtensions.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Attraction;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.SemanticMemory;
using GameEngineTools.Characters.Engines.Sleep;
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
            services.TryAddSingleton<IBehaviorCadencePolicy, DefaultBehaviorCadencePolicy>();
            services.TryAddSingleton<IMemoryFidelityPolicy, DefaultMemoryFidelityPolicy>();
            services.TryAddSingleton<IPerceptionFidelityPolicy, DefaultPerceptionFidelityPolicy>();
            services.TryAddSingleton<ISocialFidelityPolicy, DefaultSocialFidelityPolicy>();
            services.TryAddSingleton<IHumanFactory, DefaultHumanFactory>();

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
        /// Pokud potřebuješ spec sestavit až po startu DI (např. z <see cref="WorldTimeContext"/>),
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
        /// takže může záviset na libovolném singletons (typicky <see cref="WorldTimeContext"/>).
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
        /// Sdílená registrace generátorů — volaná oběma overloady <c>AddCharacterGeneration</c>.
        /// </summary>
        private static IServiceCollection AddCharacterGenerationCore(this IServiceCollection services)
        {
            services.AddSingleton<IHumanBlueprintGenerator, HumanBlueprintGenerator>();
            services.AddSingleton<IIdentityGenerator>(_ =>
            {
                var femaleNames = CsvLoader.Load(FileSystemConstant.SourceFilePath.femaleNames,
                    v => new Name { Original = v[0], Familiar = v[1].Split(' ') });
                var maleNames = CsvLoader.Load(FileSystemConstant.SourceFilePath.maleNames,
                    v => new Name { Original = v[0], Familiar = v[1].Split(' ') });
                var surnames = CsvLoader.Load(FileSystemConstant.SourceFilePath.surnames,
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

        #endregion Engine registrace

        #region Zkrácená registrace všeho najednou

        /// <summary>
        /// Zkrácená registrace všech enginů najednou.
        /// Konkrétní implementace předáváš jako generické parametry.
        /// </summary>
        /// <example>
        /// <code>
        /// services.AddCharacters&lt;
        ///     DefaultPhysiologyEngine,
        ///     DefaultPsychologyEngine,
        ///     DefaultBehaviorEngine,
        ///     DefaultInteractionEngine,
        ///     DefaultRelationshipsEngine,
        ///     DefaultMemoryEngine&gt;();
        /// </code>
        /// </example>
        public static IServiceCollection AddCharacters<TPhysio, TPsych, TBehav, TInter, TRel, TMem, TSem>(
            this IServiceCollection services,
            Action<PhysiologyConfig>? physio = null,
            Action<PsychologyConfig>? psych = null,
            Action<BehaviorConfig>? behav = null,
            Action<SleepConfig>? sleep = null,
            Action<InteractionConfig>? inter = null,
            Action<RelationshipsConfig>? rel = null,
            Action<MemoryConfig>? mem = null,
            Action<SemanticMemoryConfig>? semantic = null)
            where TPhysio : class, IPhysiologyEngine
            where TPsych : class, IPsychologyEngine
            where TBehav : class, IBehaviorEngine
            where TInter : class, IInteractionEngine
            where TRel : class, IRelationshipsEngine
            where TMem : class, IMemoryEngine
            where TSem : class, ISemanticMemoryEngine
        {
            services.AddCharactersCore()
                    .AddPhysiologyEngine<TPhysio>(physio)
                    .AddPsychologyEngine<TPsych>(psych)
                    .AddBehaviorEngine<TBehav>(behav, sleep)
                    .AddInteractionEngine<TInter>(inter)
                    .AddRelationshipsEngine<TRel>(rel)
                    .AddMemoryEngine<TMem>(mem)
                    .AddSemanticMemoryEngine<TSem>(semantic);

            return services;
        }

        #endregion Zkrácená registrace všeho najednou
    }
}
