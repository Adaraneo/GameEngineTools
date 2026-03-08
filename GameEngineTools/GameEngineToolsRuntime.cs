// GameEngineToolsRuntime.cs
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
    /// <summary>
    /// Entry point pro spuštění herního enginu. Sestavuje DI kontejner a startuje
    /// hosted services (inicializace manageru, aktivace subscribers).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pořadí DI registrací je záměrné</b> — respektuje závislosti:
    /// </para>
    /// <code>
    /// InitWorldClockConfig (z appsettings.json)
    ///       ↓
    /// WorldTimeSpec   (singleton — kalendář + jednotky)
    ///       ↓
    /// IWorldClock     (singleton — mapování Earth ↔ World)
    ///       ↓
    /// WorldTimeContext (singleton — factory, aritmetika, formátování)
    ///       ↓
    /// IClock / SystemClock (singleton — herní smyčka)
    /// </code>
    /// <para>
    /// <c>WDateTime.Use()</c> a <c>WDateTime.UseClock()</c> (globální statika)
    /// jsou záměrně odstraněny — veškerá závislost na spec jde přes DI.
    /// </para>
    /// </remarks>
    public static class GameEngineToolsRuntime
    {
        /// <summary>
        /// Sestaví DI kontejner, zaregistruje všechny služby a nastartuje hosted services.
        /// </summary>
        /// <param name="beginning">
        /// Počáteční herní čas. Pouze <see cref="WDateTime.WorldTicks"/> se předá
        /// <see cref="WorldClock.AlignNow"/> jako kotva — nepoužívají se žádné properties
        /// závislé na spec.
        /// </param>
        /// <param name="consoleLogs">Zapne výstup logů do konzole (výchozí true).</param>
        /// <param name="logsRoot">Kořenový adresář pro file logy (výchozí "logs").</param>
        /// <param name="generatedFileOptions">Volitelná konfigurace adresářů pro exportované postavy.</param>
        /// <param name="timescale">
        /// Rychlost světového času vůči reálnému (1.0 = real-time).
        /// </param>
        /// <returns>Handle na běžící runtime — disposable, při dispose zastaví host.</returns>
        public static async Task<GameEngineToolsRuntimeHandle> StartAsync(
            WDateTime beginning,
            bool consoleLogs = true,
            string logsRoot = "logs",
            GeneratedFileOptions? generatedFileOptions = null,
            double timescale = 1)
        {
            // Uložíme tiky před vstupem do DI lambdy — closure capture hodnoty, ne referenci
            var beginningTicks = beginning.WorldTicks;

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
                    var worldTypeConfig = configProvider
                        .GetSection("InitWorldClock")
                        .GetValue<string>("UseWorldType");

                    s.AddSingleton<IConfiguration>(configProvider);

                    // ── Konfigurace světového času z appsettings.json ──────────────────
                    s.AddOptionsWithValidateOnStart<InitWorldClockConfig>()
                     .Configure<IConfiguration>((opt, cfg) =>
                     {
                         opt.DaysInMonths = Array.Empty<int>();
                         cfg.GetSection($"InitWorldClock:{worldTypeConfig}").Bind(opt);
                     });

                    // ── WorldTimeSpec — jeden singleton, sdílený WorldClock i WorldTimeContext ──
                    //    Dřív: spec se vytvářel uvnitř InitWorldClock() a uložil do WDateTime.Spec (global state)
                    //    Teď:  spec žije v DI, obě třídy ho dostanou ze stejného místa
                    s.AddSingleton<WorldTimeSpec>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptions<InitWorldClockConfig>>().Value;
                        var calendar = new FixedMonthsCalendar(
                            opts.DaysInMonths,
                            y => y % opts.LeapYearInterval == 0 ? opts.LeapExtraDays : 0);

                        return new WorldTimeSpec(
                            opts.TicksPerSecond,
                            opts.SecondsPerMinute,
                            opts.MinutesPerHour,
                            opts.HoursPerDay,
                            calendar);
                    });

                    // ── IWorldClock — přijme WorldTimeSpec ze stejného DI kontejneru ──
                    //    beginningTicks = kotva "teď ve světě" při startu
                    //    Dřív: WDateTime.UseClock(worldClock) ukládal do static pole
                    s.AddSingleton<IWorldClock>(sp =>
                    {
                        var spec = sp.GetRequiredService<WorldTimeSpec>();
                        return WorldClock.AlignNow(spec, beginningTicks, timescale);
                    });

                    // ── WorldTimeContext — dostane WorldTimeSpec + IWorldClock automaticky ──
                    s.AddSingleton<WorldTimeContext>();

                    // ── IClock / SystemClock — dostane IWorldClock + WorldTimeContext ──
                    s.AddSingleton<IClock, SystemClock>();

                    // ── Soubory a volby ───────────────────────────────────────────────
                    s.AddSingleton<IGeneratedFile, GeneratedFile>();
                    s.Configure<GeneratedFileOptions>(opt =>
                    {
                        if (generatedFileOptions is not null)
                        {
                            opt.NPCDirectory = generatedFileOptions.NPCDirectory;
                            opt.PlayerDirectory = generatedFileOptions.PlayerDirectory;
                        }
                    });

                    // ── Enginy postav ─────────────────────────────────────────────────
                    s.AddCharacters<
                        DefaultPhysiologyEngine,
                        DefaultPsychologyEngine,
                        DefaultBehaviorEngine,
                        DefaultInteractionEngine,
                        DefaultRelationshipsEngine,
                        DefaultMemoryEngine>();

                    s.AddOptions<MenstrualCycleConfig>()
                     .BindConfiguration("Characters:MenstrualCycle");

                    // ── HumanBlueprintSpec — lazy factory, WorldTimeContext je k dispozici ──
                    //    Dřív: HumanBlueprintSpec.Default(initNow.DateOnly) volal WDateTime.Spec
                    //    Teď:  spec se sestaví až při prvním resolve, kdy DI má vše k dispozici
                    s.AddCharacterGeneration(sp =>
                    {
                        var ctx = sp.GetRequiredService<WorldTimeContext>();
                        var beginningDt = new WDateTime(beginningTicks);
                        return HumanBlueprintSpec.Default(ctx.GetDate(beginningDt), ctx);
                    });

                    // ── Manager a inicializace ─────────────────────────────────────────
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

    /// <summary>
    /// Handle na běžící runtime. Drží <see cref="IHost"/> a zpřístupňuje
    /// klíčové služby pro herní smyčku.
    /// </summary>
    public sealed class GameEngineToolsRuntimeHandle : IAsyncDisposable
    {
        private readonly IHost _host;

        internal GameEngineToolsRuntimeHandle(IHost host) => _host = host;

        /// <summary>Herní hodiny (aktuální čas, Start/Stop).</summary>
        public IClock Clock => Services.GetRequiredService<IClock>();

        /// <summary>Hlavní správce postav a herního světa.</summary>
        public IGameEngineToolsManager GameEngineToolsManager
            => Services.GetRequiredService<IGameEngineToolsManager>();

        /// <summary>DI provider pro přímý resolve libovolné služby.</summary>
        public IServiceProvider Services => _host.Services;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
