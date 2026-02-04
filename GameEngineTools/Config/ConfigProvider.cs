// ConfigProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Config
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Configuration;

    public static class ConfigProvider
    {
        private static readonly Lazy<IConfiguration> lazyConfig = new(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Characters.json");
            return builder.Build();
        });

        public static IConfiguration Configuration => lazyConfig.Value;
    }
}
