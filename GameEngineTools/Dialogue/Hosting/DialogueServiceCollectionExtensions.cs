// DialogueServiceCollectionExtensions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Hosting
{
    using GameEngineTools.Dialogue.Planning;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>DI registration for the dialogue engine's speaker-side services.</summary>
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
    }
}
