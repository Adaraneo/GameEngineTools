// ServiceCollectionExtensions.cs
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameEngineTools.Characters.Hosting
{

    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Zaregistruje jádro (EventBus, Scheduler, RNG factory, HumanFactory).
        /// Defaulty jsou "in-memory" a dají se přepsat vlastními implementacemi dřív/nebo později v pipeline.
        /// </summary>
        public static IServiceCollection AddCharactersCore(this IServiceCollection services)
        {
            services.TryAddTransient<IEventBus, InMemoryEventBus>();
            services.TryAddTransient<IScheduler, SimpleScheduler>();
            services.TryAddSingleton<IRandomSourceFactory, RandomSourceFactory>();

            services.TryAddSingleton<IHumanFactory, DefaultHumanFactory>();

            return services;
        }

        private static IServiceCollection AddPersonalityGenerator(this IServiceCollection services)
        => services.AddSingleton<IPersonalityGenerator, PersonalityGenerator>();

        private static IServiceCollection AddAppearanceGenerator(this IServiceCollection services)
        => services.AddSingleton<IAppearanceGenerator, AppearanceGenerator>();

        public static IServiceCollection AddCharacterGeneration(this IServiceCollection services, HumanBlueprintSpec humanBlueprintSpec)
        {
            services.AddSingleton(humanBlueprintSpec);
            services.AddSingleton<IHumanBlueprintGenerator, HumanBlueprintGenerator>();
            services.AddSingleton<IIdentityGenerator>(_ =>
                new SimpleIdentityGenerator());
            services.AddAppearanceGenerator();
            services.AddPersonalityGenerator();
            return services;
        }

        /// <summary>Registrace implementace Physiology engine + jeho Configu přes Options.</summary>
        public static IServiceCollection AddPhysiologyEngine<TImpl>(
            this IServiceCollection services,
            Action<PhysiologyConfig>? configure = null)
            where TImpl : class, IPhysiologyEngine
        {
            services.AddTransient<IPhysiologyEngine, TImpl>();
            var ob = services.AddOptions<PhysiologyConfig>();
            if (configure != null) ob.Configure(configure);
            else ob.BindConfiguration("Characters:Physiology");
            return services;
        }

        /// <summary>Registrace implementace Psychology engine + jeho Configu přes Options.</summary>
        public static IServiceCollection AddPsychologyEngine<TImpl>(
            this IServiceCollection services,
            Action<PsychologyConfig>? configure = null)
            where TImpl : class, IPsychologyEngine
        {
            services.AddTransient<IPsychologyEngine, TImpl>();
            var ob = services.AddOptions<PsychologyConfig>();
            if (configure != null) ob.Configure(configure);
            else ob.BindConfiguration("Characters:Psychology");
            return services;
        }

        /// <summary>Registrace implementace Behavior engine + jeho Configu přes Options.</summary>
        public static IServiceCollection AddBehaviorEngine<TImpl>(
            this IServiceCollection services,
            Action<BehaviorConfig>? configure = null)
            where TImpl : class, IBehaviorEngine
        {
            services.AddTransient<IBehaviorEngine, TImpl>();
            var ob = services.AddOptions<BehaviorConfig>();
            if (configure != null) ob.Configure(configure);
            else ob.BindConfiguration("Characters:Behavior");
            return services;
        }

        /// <summary>Registrace implementace Interactions engine + jeho Configu přes Options.</summary>
        public static IServiceCollection AddInteractionEngine<TImpl>(
            this IServiceCollection services,
            Action<InteractionConfig>? configure = null)
            where TImpl : class, IInteractionEngine
        {
            services.AddTransient<IInteractionEngine, TImpl>();
            var ob = services.AddOptions<InteractionConfig>();
            if (configure != null) ob.Configure(configure);
            else ob.BindConfiguration("Characters:Interactions");
            return services;
        }

        /// <summary>Registrace implementace Relationships engine + jeho Configu přes Options.</summary>
        public static IServiceCollection AddRelationshipsEngine<TImpl>(
            this IServiceCollection services,
            Action<RelationshipsConfig>? configure = null)
            where TImpl : class, IRelationshipsEngine
        {
            services.AddTransient<IRelationshipsEngine, TImpl>();
            var ob = services.AddOptions<RelationshipsConfig>();
            if (configure != null) ob.Configure(configure);
            else ob.BindConfiguration("Characters:Relationships");
            return services;
        }

        /// <summary>Registrace implementace Memory engine + jeho Configu přes Options.</summary>
        public static IServiceCollection AddMemoryEngine<TImpl>(
            this IServiceCollection services,
            Action<MemoryConfig>? configure = null)
            where TImpl : class, IMemoryEngine
        {
            services.AddTransient<IMemoryEngine, TImpl>();
            var ob = services.AddOptions<MemoryConfig>();
            if (configure != null) ob.Configure(configure);
            else ob.BindConfiguration("Characters:Memory");
            return services;
        }

        /// <summary>
        /// Kompletní registrace „všeho“ najednou (krom tvých konkrétních implementací enginů, ty předáš generiky).
        /// Příklad použití viz XML doc summary u <see cref="IHumanFactory"/>.
        /// </summary>
        public static IServiceCollection AddCharacters<TPhysio, TPsych, TBehav, TInter, TRel, TMem>(
            this IServiceCollection services,
            Action<PhysiologyConfig>? physio = null,
            Action<PsychologyConfig>? psych = null,
            Action<BehaviorConfig>? behav = null,
            Action<InteractionConfig>? inter = null,
            Action<RelationshipsConfig>? rel = null,
            Action<MemoryConfig>? mem = null)
            where TPhysio : class, IPhysiologyEngine
            where TPsych : class, IPsychologyEngine
            where TBehav : class, IBehaviorEngine
            where TInter : class, IInteractionEngine
            where TRel : class, IRelationshipsEngine
            where TMem : class, IMemoryEngine
        {
            services.AddCharactersCore()
                    .AddPhysiologyEngine<TPhysio>(physio)
                    .AddPsychologyEngine<TPsych>(psych)
                    .AddBehaviorEngine<TBehav>(behav)
                    .AddInteractionEngine<TInter>(inter)
                    .AddRelationshipsEngine<TRel>(rel)
                    .AddMemoryEngine<TMem>(mem);

            return services;
        }
    }
}
