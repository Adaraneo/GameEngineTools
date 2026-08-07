// ConfigProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Config
{
    using System;
    using Microsoft.Extensions.Configuration;

    /// <summary>Lazily-built shared <see cref="IConfiguration"/> from the appsettings JSON files.</summary>
    public static class ConfigProvider
    {
        private static readonly Lazy<IConfiguration> lazyConfig = new(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Characters.json")
                .AddJsonFile("appsettings.World.json")
                .AddJsonFile("appsettings.Economy.json");
            return builder.Build();
        });

        /// <summary>The shared configuration, built on first access.</summary>
        public static IConfiguration Configuration => lazyConfig.Value;
    }
}
