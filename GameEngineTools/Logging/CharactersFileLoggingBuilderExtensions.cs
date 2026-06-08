// CharactersFileLoggingBuilderExtensions.cs
// Copyright (c) 50PSoftware

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Extension metody pro registraci character file loggeru do DI.
    /// </summary>
    public static class CharactersFileLoggingBuilderExtensions
    {
        /// <summary>
        /// Adds the character file logger to the logging pipeline.
        /// </summary>
        public static ILoggingBuilder AddCharactersFile(
            this ILoggingBuilder builder,
            Action<CharactersFileLoggerOptions>? configure = null)
        {
            var opt = new CharactersFileLoggerOptions();
            configure?.Invoke(opt);

            builder.Services.AddSingleton(opt);
            builder.Services.AddSingleton<CharactersFileLoggerProvider>();
            builder.Services.AddSingleton<ICharactersLogControl>(sp => sp.GetRequiredService<CharactersFileLoggerProvider>());
            builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<CharactersFileLoggerProvider>());

            builder.AddFilter<CharactersFileLoggerProvider>(level => level >= opt.MinLevel);

            return builder;
        }
    }
}
