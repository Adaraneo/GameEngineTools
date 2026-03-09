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
    /// IClock / SystemClock (singleton — herní smyčka, bere WorldTimeSpec přímo)
    ///       ↓
    /// WorldTimeContext (singleton — legacy wrapper, bere IClock.Now přes WWorld)
    ///       ↓
    /// WWorld.Configure(spec, clock)  ← ambient konfigurace pro W-typy
    /// </code>
    /// <para>
    /// Od redesignu (Varianta A — Ambient Spec) jsou W-typy (<see cref="WDateTime"/> atd.)
    /// konfigurovány přes <see cref="WWorld"/>. Volání <c>dt.Year</c>, <c>dt.AddMonths(3)</c>
    /// atd. fungují bez předávání kontextu — stejně jako <see cref="DateTime"/>.
    /// </para>
    /// </remarks>
    public static class GameEngineToolsRuntime
    {
        #region LoadSpec

        /// <summary>
        /// Načte <see cref="WorldTimeSpec"/> z konfigurace — bez spouštění DI kontejneru.
        /// </summary>
        /// <remarks>
        /// Použij tehdy, když potřebuješ sestavit <see cref="WDateTime"/> (nebo vypočítat tiky)
        /// <b>před</b> voláním <see cref="StartAsync"/> — typicky při načítání uloženého stavu hry.
        /// <code>
        /// var spec   = GameEngineToolsRuntime.LoadSpec();
        /// long ticks = long.TryParse(saved, out var t) ? t
        ///            : spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;
        ///
        /// await using var runtime = await GameEngineToolsRuntime.StartAsync(new WDateTime(ticks));
        /// </code>
        /// Runtime interně volá stejný kód — spec je zaručeně identický s tím v DI.
        /// </remarks>
        /// <returns><see cref="WorldTimeSpec"/> sestavený z <c>appsettings.json</c>.</returns>
        public static WorldTimeSpec LoadSpec()
        {
            var config          = ConfigProvider.Configuration;
            var worldTypeConfig = config.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            var opts            = config
                .GetSection($"InitWorldClock:{worldTypeConfig}")
                .Get<InitWorldClockConfig>()!;

            var calendar = new FixedMonthsCalendar(
                opts.DaysInMonths,
                y => y % opts.LeapYearInterval == 0 ? opts.LeapExtraDays : 0);

            return new WorldTimeSpec(
                opts.TicksPerSecond,
                opts.SecondsPerMinute,
                opts.MinutesPerHour,
                opts.HoursPerDay,
                calendar);
        }

        #endregion

        #region StartAsync

        /// <summary>
        /// Sestaví DI kontejner, zaregistruje všechny služby, nakonfiguruje
        /// <see cref="WWorld"/> a nastartuje hosted services.
        /// </summary>
        /// <param name="beginning">
        /// Počáteční herní čas. Pouze <see cref="WDateTime.WorldTicks"/> se předá
        /// <see cref="WorldClock.AlignNow"/> jako kotva.
        /// </param>
        /// <param name="consoleLogs">Zapne výstup logů do konzole (výchozí true).</param>
        /// <param name="logsRoot">Kořenový adresář pro file logy (výchozí "logs").</param>
        /// <param name="generatedFileOptions">Volitelná konfigurace adresářů pro exportované postavy.</param>
        /// <param name="timescale">Rychlost světového času vůči reálnému (1.0 = real-time).</param>
        /// <returns>Handle na běžící runtime — disposable, při dispose zastaví host.</returns>
        public static async Task<GameEngineToolsRuntimeHandle> StartAsync(
            WDateTime             beginning,
            bool                  consoleLogs          = true,
            string                logsRoot             = "logs",
            GeneratedFileOptions? generatedFileOptions = null,
            double                timescale            = 1)
        {
            // Uložíme tiky před vstupem do DI lambdy — closure capture hodnoty, ne referenci
            var beginningTicks = beginning.WorldTicks;

            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(lb =>
                {
                    lb.ClearProviders();
                    if (consoleLogs)
                        lb.AddConsole();

                    lb.AddCharactersFile(opt =>
                    {
                        opt.FilePath         = "logs/Characters/characters.log";
                        opt.MinLevel         = LogLevel.Debug;
                        opt.UseUtcTimestamps = true;
                    });
                })
                .ConfigureServices(s =>
                {
                    var configProvider  = ConfigProvider.Configuration;
                    var worldTypeConfig = configProvider
                        .GetSection("InitWorldClock")
                        .GetValue<string>("UseWorldType");

                    s.AddSingleton<IConfiguration>(configProvider);

                    // ── WorldTimeSpec — jeden singleton, sdílený WorldClock i WorldTimeContext ──
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

                    s.AddOptionsWithValidateOnStart<InitWorldClockConfig>()
                     .Configure<IConfiguration>((opt, cfg) =>
                     {
                         opt.DaysInMonths = Array.Empty<int>();
                         cfg.GetSection($"InitWorldClock:{worldTypeConfig}").Bind(opt);
                     });

                    // ── IWorldClock — kotva na beginningTicks, mapování real-time → world-time ──
                    s.AddSingleton<IWorldClock>(sp =>
                    {
                        var spec = sp.GetRequiredService<WorldTimeSpec>();
                        return WorldClock.AlignNow(spec, beginningTicks, timescale);
                    });

                    // ── IClock / SystemClock — bere WorldTimeSpec (ne WorldTimeContext!)
                    //    Důvod: SystemClock → WorldTimeContext → IClock by byl kruh.
                    //    WorldTimeSpec je čistý datový objekt — žádná závislost.
                    s.AddSingleton<IClock, SystemClock>();

                    // ── Soubory a volby ───────────────────────────────────────────────
                    s.AddSingleton<IGeneratedFile, GeneratedFile>();
                    s.Configure<GeneratedFileOptions>(opt =>
                    {
                        if (generatedFileOptions is not null)
                        {
                            opt.NPCDirectory    = generatedFileOptions.NPCDirectory;
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

                    // ── HumanBlueprintSpec — lazy factory ─────────────────────────────
                    //    WDateTime.New() funguje jakmile WWorld je nakonfigurován
                    //    (volá se níže ihned po sestavení hostu).
                    s.AddCharacterGeneration(sp =>
                    {
                        var clock = sp.GetRequiredService<IClock>();
                        return HumanBlueprintSpec.Default(clock.Now.Date);
                    });

                    // ── Manager a inicializace ─────────────────────────────────────────
                    s.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
                    s.Configure<GameEngineToolsManagerOptions>(opt =>
                    {
                        opt.UseConsoleLogging = consoleLogs;
                        opt.LogsRoot          = logsRoot;
                    });
                    s.AddHostedService<GameEngineToolsManagerInitializer>();
                    s.AddHostedService<SubscribersActivator>();
                })
                .Build();

            // ── WWorld.Configure — ambient konfigurace pro W-typy ──────────────
            // Musí proběhnout PŘED StartAsync — hosted services mohou volat W-typy.
            var spec  = host.Services.GetRequiredService<WorldTimeSpec>();
            var clock = host.Services.GetRequiredService<IClock>();
            WWorld.Configure(spec, clock);

            await host.StartAsync();
            return new GameEngineToolsRuntimeHandle(host);
        }

        #endregion
    }

    /// <summary>
    /// Handle na běžící runtime. Drží <see cref="IHost"/> a zpřístupňuje
    /// klíčové služby pro herní smyčku.
    /// </summary>
    public sealed class GameEngineToolsRuntimeHandle : IAsyncDisposable
    {
        #region Soukromá pole

        private readonly IHost _host;

        #endregion

        #region Konstrukce

        internal GameEngineToolsRuntimeHandle(IHost host) => _host = host;

        #endregion

        #region Veřejné vlastnosti

        /// <summary>Herní hodiny — aktuální čas, Start/Stop pro game loop.</summary>
        public IClock Clock => Services.GetRequiredService<IClock>();

        /// <summary>Hlavní správce postav a herního světa.</summary>
        public IGameEngineToolsManager GameEngineToolsManager
            => Services.GetRequiredService<IGameEngineToolsManager>();

        /// <summary>DI provider pro přímý resolve libovolné služby.</summary>
        public IServiceProvider Services => _host.Services;

        #endregion

        #region IAsyncDisposable

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        #endregion
    }
}
