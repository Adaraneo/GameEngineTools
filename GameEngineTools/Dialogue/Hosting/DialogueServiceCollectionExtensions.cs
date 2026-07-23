// DialogueServiceCollectionExtensions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Hosting
{
    using GameEngineTools.Dialogue.Interpretation;
    using GameEngineTools.Dialogue.Planning;
    using GameEngineTools.Dialogue.Semantics;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>DI registration for the dialogue engine's speaker- and listener-side services.</summary>
    public static class DialogueServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the <see cref="ISpeechActPlanner"/> (stateless, singleton). Pass a
        /// <paramref name="config"/> to override the default register/directness calibration.
        /// </summary>
        public static IServiceCollection AddDialoguePlanner(
            this IServiceCollection services,
            SpeechActPlannerConfig? config = null)
        {
            services.AddSingleton<ISpeechActPlanner>(_ => new DefaultSpeechActPlanner(config));
            return services;
        }

        /// <summary>
        /// Registers the <see cref="ISpeechActInterpreter"/> (stateless, singleton) together with the
        /// curated <see cref="IConnotationLexicon"/>. Pass a <paramref name="config"/> to override the
        /// default irony/hostility calibration — the connotation layer stays opt-in
        /// (<see cref="SpeechActInterpreterConfig.EnableConnotationLayer"/> defaults to <c>false</c>).
        /// </summary>
        public static IServiceCollection AddSpeechActInterpreter(
            this IServiceCollection services,
            SpeechActInterpreterConfig? config = null)
        {
            services.AddSingleton<IConnotationLexicon, CuratedConnotationLexicon>();
            services.AddSingleton<ISpeechActInterpreter>(
                sp => new DefaultSpeechActInterpreter(config, sp.GetRequiredService<IConnotationLexicon>()));
            return services;
        }
    }
}
