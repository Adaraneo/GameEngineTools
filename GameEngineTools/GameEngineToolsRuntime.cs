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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameEngineTools
{
    /// <summary>
    /// Entry point pro spuštění herního enginu.
    /// Sestavuje DI kontejner přes <see cref="ServiceCollection"/> a inicializuje
    /// všechny registrované služby bez overhead <c>IHost</c> / <c>IHostedService</c> pumpy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proč bez IHost?</b><br/>
    /// <c>Microsoft.Extensions.Hosting.IHost</c> přidává:
    /// <list type="bullet">
    ///   <item>Graceful shutdown signály (<c>IHostApplicationLifetime</c>)</item>
    ///   <item>Environment detection (Development / Production)</item>
    ///   <item>Background service pumpu pro web / worker scénáře</item>
    /// </list>
    /// V herní smyčce (Unity / standalone) žádnou z těchto věcí nepotřebujeme.
    /// Stačí nám <see cref="ServiceCollection"/> + ruční zavolání <c>Initialize()</c>
    /// na manageru — přesně to, co dělal host interně.
    /// </para>
    /// <para>
    /// <b>Závislostní graf DI registrací:</b>
    /// </para>
    /// <code>
    /// InitWorldClockConfig  (z appsettings.json)
    ///       ↓
    /// WorldTimeSpec         (singleton — kalendář + jednotky)
    ///       ↓
    /// IWorldClock           (singleton — mapování Earth ↔ World)
    ///       ↓
    /// IClock / SystemClock  (singleton — herní smyčka, bere WorldTimeSpec přímo)
    ///       ↓
    /// WorldTimeContext      (singleton — legacy wrapper)
    ///       ↓
    /// WWorld.Configure(spec, clock)  ← ambient konfigurace pro W-typy
    /// </code>
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
        /// <returns>Plně sestavená <see cref="WorldTimeSpec"/> z aktuální konfigurace.</returns>
        public static WorldTimeSpec LoadSpec()
        {
            // Načteme konfiguraci stejným způsobem jako StartAsync,
            // aby spec byl zaručeně identický — žádné duplicitní hodnoty.
            var cfg          = ConfigProvider.Configuration;
            var worldType    = cfg.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            var opts         = new InitWorldClockConfig();
            opts.DaysInMonths = Array.Empty<int>();
            cfg.GetSection($"InitWorldClock:{worldType}").Bind(opts);

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
        /// Sestaví DI kontejner, nakonfiguruje <see cref="WWorld"/> a inicializuje
        /// herní engine. Vrátí handle pro přístup ke službám za běhu.
        /// </summary>
        /// <param name="startTime">
        /// Volitelný počáteční čas světa. Pokud <c>null</c>, použije se
        /// výchozí začátek roku 1322 definovaný v konfiguraci.
        /// </param>
        /// <param name="consoleLogs">Zapne konzolové logování v manageru.</param>
        /// <param name="logsRoot">Kořenový adresář pro logové soubory.</param>
        /// <param name="generatedFileOptions">Volitelná konfigurace adresářů pro generované soubory.</param>
        /// <returns>
        /// <see cref="GameEngineToolsRuntimeHandle"/> — handle na běžící runtime.
        /// Dispose zastaví všechny služby a uvolní DI kontejner.
        /// </returns>
        public static async Task<GameEngineToolsRuntimeHandle> StartAsync(
            WDateTime?           startTime            = null,
            bool                 consoleLogs          = false,
            string?              logsRoot             = null,
            GeneratedFileOptions? generatedFileOptions = null)
        {
            var services = new ServiceCollection();

            // ── Logging ───────────────────────────────────────────────────────
            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddConsole();
                lb.AddCharactersFile(opt =>
                {
                    opt.FilePath         = logsRoot != null
                        ? Path.Combine(logsRoot, "Characters", "characters.log")
                        : "logs/Characters/characters.log";
                    opt.MinLevel         = LogLevel.Debug;
                    opt.UseUtcTimestamps = true;
                });
            });

            // ── Konfigurace ───────────────────────────────────────────────────
            var configProvider  = ConfigProvider.Configuration;
            var worldTypeConfig = configProvider.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            services.AddSingleton<IConfiguration>(configProvider);

            services.AddOptionsWithValidateOnStart<InitWorldClockConfig>()
                    .Configure<IConfiguration>((opt, cfg) =>
                    {
                        opt.DaysInMonths = Array.Empty<int>();
                        cfg.GetSection($"InitWorldClock:{worldTypeConfig}").Bind(opt);
                    });

            // ── WorldTimeSpec — singleton ─────────────────────────────────────
            services.AddSingleton<WorldTimeSpec>(sp =>
            {
                var opts     = sp.GetRequiredService<IOptions<InitWorldClockConfig>>().Value;
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

            // ── IWorldClock — mapování Earth time → World time ─────────────────
            services.AddSingleton<IWorldClock>(sp =>
            {
                var wSpec = sp.GetRequiredService<WorldTimeSpec>();

                // Pokud caller předal startTime, použijeme jeho tiky.
                // Jinak začínáme na rok 1322, 1. den, 1. měsíc — definovaný začátek světa.
                long beginningTicks = startTime.HasValue
                    ? startTime.Value.WorldTicks
                    : wSpec.Calendar.DaysFromDate(1, 1, 1) * wSpec.TicksPerDay;

                return WorldClock.AlignNow(wSpec, beginningTicks);
            });

            // ── IClock / SystemClock ──────────────────────────────────────────
            services.AddSingleton<IClock, SystemClock>();

            // ── Soubory a volby ───────────────────────────────────────────────
            services.AddSingleton<IGeneratedFile, GeneratedFile>();
            services.Configure<GeneratedFileOptions>(opt =>
            {
                if (generatedFileOptions is not null)
                {
                    opt.NPCDirectory    = generatedFileOptions.NPCDirectory;
                    opt.PlayerDirectory = generatedFileOptions.PlayerDirectory;
                }
            });

            // ── Enginy postav ─────────────────────────────────────────────────
            services.AddCharacters<
                DefaultPhysiologyEngine,
                DefaultPsychologyEngine,
                DefaultBehaviorEngine,
                DefaultInteractionEngine,
                DefaultRelationshipsEngine,
                DefaultMemoryEngine>();

            services.AddOptions<MenstrualCycleConfig>()
                    .BindConfiguration("Characters:MenstrualCycle");

            // ── HumanBlueprintSpec — lazy factory ─────────────────────────────
            services.AddCharacterGeneration(sp =>
            {
                var clock = sp.GetRequiredService<IClock>();
                return HumanBlueprintSpec.Default(clock.Now.Date);
            });

            // ── Manager ───────────────────────────────────────────────────────
            services.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
            services.Configure<GameEngineToolsManagerOptions>(opt =>
            {
                opt.UseConsoleLogging = consoleLogs;
                opt.LogsRoot          = logsRoot;
            });

            // ── Sestavení DI kontejneru ───────────────────────────────────────
            var provider = services.BuildServiceProvider();

            // ── WWorld.Configure — ambient konfigurace pro W-typy ─────────────
            var spec  = provider.GetRequiredService<WorldTimeSpec>();
            var clock = provider.GetRequiredService<IClock>();
            WWorld.Configure(spec, clock);

            // ── Inicializace manageru ─────────────────────────────────────────
            var manager = provider.GetRequiredService<IGameEngineToolsManager>();
            manager.Initialize();

            return new GameEngineToolsRuntimeHandle(provider);
        }

        #endregion
    }

    /// <summary>
    /// Handle na běžící runtime. Drží <see cref="IServiceProvider"/> a zpřístupňuje
    /// klíčové služby pro herní smyčku.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementuje <see cref="IAsyncDisposable"/> — dispose zastaví subscribery
    /// a uvolní DI kontejner (a všechny <see cref="IDisposable"/> singletony v něm).
    /// </para>
    /// <para>
    /// Doporučené použití:
    /// <code>
    /// await using var runtime = await GameEngineToolsRuntime.StartAsync();
    /// var manager = runtime.GameEngineToolsManager;
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class GameEngineToolsRuntimeHandle : IAsyncDisposable
    {
        #region Soukromá pole

        /// <summary>
        /// DI provider sestavený v <see cref="GameEngineToolsRuntime.StartAsync"/>.
        /// Uložíme ho jako <see cref="ServiceProvider"/> (konkrétní typ), protože
        /// potřebujeme <see cref="ServiceProvider.DisposeAsync"/> — <see cref="IServiceProvider"/>
        /// tento interface neimplementuje.
        /// </summary>
        private readonly ServiceProvider _provider;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Interní konstruktor — volá ho pouze <see cref="GameEngineToolsRuntime.StartAsync"/>.
        /// </summary>
        /// <param name="provider">Plně sestavený DI kontejner.</param>
        internal GameEngineToolsRuntimeHandle(ServiceProvider provider)
            => _provider = provider;

        #endregion

        #region Veřejné vlastnosti

        /// <summary>
        /// Herní hodiny — aktuální čas, možnost pozastavit / přepnout rychlost.
        /// </summary>
        public IClock Clock
            => _provider.GetRequiredService<IClock>();

        /// <summary>
        /// Hlavní správce postav a herního světa.
        /// </summary>
        public IGameEngineToolsManager GameEngineToolsManager
            => _provider.GetRequiredService<IGameEngineToolsManager>();

        /// <summary>
        /// DI provider pro přímý resolve libovolné služby.
        /// Používej výjimečně — preferuj typované vlastnosti výše.
        /// </summary>
        public IServiceProvider Services => _provider;

        #endregion

        #region IAsyncDisposable

        /// <summary>
        /// Uvolní DI kontejner a všechny <see cref="IDisposable"/> / <see cref="IAsyncDisposable"/>
        /// singletony v něm registrované. Žádné hosted services k zastavení — to bylo
        /// odstraněno spolu s <c>IHost</c>.
        /// </summary>
        public async ValueTask DisposeAsync()
            => await _provider.DisposeAsync().ConfigureAwait(false);

        #endregion
    }
}
