using GameEngineTools.Config;
using Microsoft.Extensions.Configuration;

namespace EngineTests.Config
{
    public static class ConfigProvider
    {
        private static readonly Lazy<IConfiguration> lazyConfig = new(() =>
        {
            var dir = Directory.GetCurrentDirectory();
            var builder = new ConfigurationBuilder()
                .SetBasePath(dir)
                .AddJsonFile("appsettings.Test.json")
                .AddJsonFile("appsettings.Characters.json")
                .AddWorldFile(Path.Combine(dir, WorldSettings.BaseFileName))
                .AddJsonFile("appsettings.Economy.json");
            return builder.Build();
        });

        public static IConfiguration Configuration => lazyConfig.Value;
    }
}
