using Microsoft.Extensions.Configuration;

namespace EngineTests.Config
{
    public static class ConfigProvider
    {
        private static readonly Lazy<IConfiguration> lazyConfig = new(() =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Test.json")
                .AddJsonFile("appsettings.Characters.json")
                .AddJsonFile("appsettings.World.json")
                .AddJsonFile("appsettings.Economy.json");
            return builder.Build();
        });

        public static IConfiguration Configuration => lazyConfig.Value;
    }
}
