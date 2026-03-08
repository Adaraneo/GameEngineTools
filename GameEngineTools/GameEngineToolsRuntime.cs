// CharactersRuntime.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Characters.Hosting;
using GameEngineTools.Config;
using GameEngineTools.FileSystem;
using GameEngineTools.Logging;
using GameEngineTools.World.Core.Calendars;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameEngineTools
{
    public static class GameEngineToolsRuntime
    {
        private static IWorldClock DefaultWorldClock(IOptions<InitWorldClockConfig> opt, WDateTime now, double timescale = 1) => InitWorldClock(opt, now, timescale);

        private static IWorldClock InitWorldClock(IOptions<InitWorldClockConfig> opts, WDateTime now, double timescale = 1)
        {
            var calendar = new FixedMonthsCalendar(opts.Value.DaysInMonths, y => y % opts.Value.LeapYearInterval == 0 ? opts.Value.LeapExtraDays : 0);
            var spec = new WorldTimeSpec(opts.Value.TicksPerSecond, opts.Value.SecondsPerMinute, opts.Value.MinutesPerHour, opts.Value.HoursPerDay, calendar);
            WDateTime.Use(spec);
            var worldClock = WorldClock.AlignNow(spec, now, timescale);
            WDateTime.UseClock(worldClock);
            return worldClock;
        }

        public static async Task<GameEngineToolsRuntimeHandle> StartAsync(HumanBlueprintSpec humanBlueprintSpec, WDateTime beginning, bool consoleLogs = true, string logsRoot = "logs", GeneratedFileOptions? generatedFileOptions = null, double timescale = 1)
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(lb =>
                {
                    lb.ClearProviders();
                    if (consoleLogs)
                    {
                        lb.AddConsole();
                    }
                    lb.AddCharactersFile(opt =>
                    {
                        opt.FilePath = "logs/Characters/characters.log";
                        opt.MinLevel = LogLevel.Debug;
                        opt.UseUtcTimestamps = true;
                    });
                })
                .ConfigureServices(s =>
                {
                    var configProvider = ConfigProvider.Configuration;
                    var worldTypeConfig = configProvider.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
                    s.AddSingleton<IConfiguration>(configProvider);
                    s.AddOptionsWithValidateOnStart<InitWorldClockConfig>().Configure<IConfiguration>((opt, cfg) =>
                    {
                        opt.DaysInMonths = Array.Empty<int>();
                        cfg.GetSection($"InitWorldClock:{worldTypeConfig}").Bind(opt);
                    });

                    s.AddSingleton<IWorldClock>(sp => DefaultWorldClock(sp.GetRequiredService<IOptions<InitWorldClockConfig>>(), beginning, timescale));
                    s.AddSingleton<IClock, SystemClock>();
                    s.AddSingleton<IGeneratedFile, GeneratedFile>();
                    s.Configure<GeneratedFileOptions>(opt =>
                    {
                        if (generatedFileOptions != null)
                        {
                            opt.NPCDirectory = generatedFileOptions.NPCDirectory;
                            opt.PlayerDirectory = generatedFileOptions.PlayerDirectory;
                        }
                    });

                    s.AddCharacters<
                        DefaultPhysiologyEngine,
                        DefaultPsychologyEngine,
                        DefaultBehaviorEngine,
                        DefaultInteractionEngine,
                        DefaultRelationshipsEngine,
                        DefaultMemoryEngine
                        >();

                    s.AddOptions<MenstrualCycleConfig>().BindConfiguration("Characters:MenstrualCycle");

                    s.AddCharacterGeneration(humanBlueprintSpec);

                    s.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
                    s.Configure<GameEngineToolsManagerOptions>(opt =>
                    {
                        opt.UseConsoleLogging = consoleLogs;
                        opt.LogsRoot = logsRoot;
                    });
                    s.AddHostedService<GameEngineToolsManagerInitializer>();

                    s.AddHostedService<SubscribersActivator>();
                })
                .Build();

            await host.StartAsync();
            return new GameEngineToolsRuntimeHandle(host);
        }
    }

    public sealed class GameEngineToolsRuntimeHandle : IAsyncDisposable
    {
        private readonly IHost _host;

        internal GameEngineToolsRuntimeHandle(IHost host) => _host = host;
        public IClock Clock => Services.GetRequiredService<IClock>();
        public IGameEngineToolsManager GameEngineToolsManager => Services.GetRequiredService<IGameEngineToolsManager>();
        public IServiceProvider Services => _host.Services;

        public async ValueTask DisposeAsync()
        { await _host.StopAsync(); _host.Dispose(); }
    }
}
