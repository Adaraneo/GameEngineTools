// GameEngineToolsRuntime.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Goals;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Psychology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.Schedule;
using GameEngineTools.Characters.Engines.SemanticMemory;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Characters.Hosting;
using GameEngineTools.Config;
using GameEngineTools.Constants;
using GameEngineTools.FileSystem;
using GameEngineTools.Logging;
using GameEngineTools.World.Core.Calendars;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Data;
using GameEngineTools.World.Location;
using GameEngineTools.World.Movement;
using GameEngineTools.World.Objects;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameEngineTools
{
    /// <summary>
    /// Entry point for starting the game engine.
    /// Builds the DI container via <see cref="ServiceCollection"/> and initializes
    /// all registered services without the overhead of the <c>IHost</c> / <c>IHostedService</c> machinery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why without IHost?</b><br/>
    /// <c>Microsoft.Extensions.Hosting.IHost</c> adds:
    /// <list type="bullet">
    ///   <item>Graceful-shutdown signals (<c>IHostApplicationLifetime</c>)</item>
    ///   <item>Environment detection (Development / Production)</item>
    ///   <item>A background-service pump for web / worker scenarios</item>
    /// </list>
    /// In a game loop (Unity / standalone) we need none of these.
    /// A <see cref="ServiceCollection"/> plus a manual call to <c>Initialize()</c>
    /// on the manager is enough — exactly what the host did internally.
    /// </para>
    /// <para>
    /// <b>Dependency graph of the DI registrations:</b>
    /// </para>
    /// <code>
    /// InitWorldClockConfig  (z appsettings.json)
    ///       ↓
    /// WorldTimeSpec         (singleton — calendar + units)
    ///       ↓
    /// IWorldClock           (singleton — Earth ↔ World mapping)
    ///       ↓
    /// IClock / SystemClock  (singleton — game loop, takes WorldTimeSpec directly)
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
        /// Loads the <see cref="WorldTimeSpec"/> from configuration — without starting the DI container.
        /// </summary>
        /// <remarks>
        /// Use it when you need to build a <see cref="WDateTime"/> (or compute ticks)
        /// <b>before</b> calling <see cref="StartAsync"/> — typically when loading a saved game state.
        /// <code>
        /// var spec   = GameEngineToolsRuntime.LoadSpec();
        /// long ticks = long.TryParse(saved, out var t) ? t
        ///            : spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;
        ///
        /// await using var runtime = await GameEngineToolsRuntime.StartAsync(new WDateTime(ticks));
        /// </code>
        /// The runtime calls the same code internally — the spec is guaranteed identical to the one in DI.
        /// </remarks>
        /// <returns>A fully built <see cref="WorldTimeSpec"/> from the current configuration.</returns>
        public static WorldTimeSpec LoadSpec()
        {
            // Load the configuration the same way as StartAsync,
            // so the spec is guaranteed identical — no duplicated values.
            var cfg = ConfigProvider.Configuration;
            var worldType = cfg.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            var opts = new InitWorldClockConfig();
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

        #endregion LoadSpec

        #region StartAsync

        /// <summary>
        /// Builds the DI container, configures <see cref="WWorld"/> and initializes
        /// the game engine. Returns a handle for accessing services at runtime.
        /// </summary>
        /// <param name="consoleLogs">Enables console logging in the manager.</param>
        /// <param name="logsRoot">Root directory for log files.</param>
        /// <param name="generatedFileOptions">Optional configuration of directories for generated files.</param>
        /// <returns>
        /// <see cref="GameEngineToolsRuntimeHandle"/> — a handle to the running runtime.
        /// Dispose stops all services and releases the DI container.
        /// </returns>
        public static async Task<GameEngineToolsRuntimeHandle> StartAsync(
            bool consoleLogs = false,
            string? logsRoot = null,
            bool writeJsonLines = true,
            bool writeTextLogs = true,
            GeneratedFileOptions? generatedFileOptions = null)
        {
            var services = new ServiceCollection();

            // ── Logging ───────────────────────────────────────────────────────
            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                if (consoleLogs) lb.AddConsole();
                lb.AddCharactersFile(opt =>
                {
                    opt.LogsDirectoryPath = logsRoot != null
                        ? Path.Combine(logsRoot, "Characters")
                        : Path.Combine("logs", "Characters");
                    opt.MinLevel = LogLevel.Debug;
                    opt.UseUtcTimestamps = true;
                    opt.WorldTimeTextAccessor = () => WDateTime.Now.ToString();
                    opt.WorldTicksAccessor = () => WDateTime.Now.WorldTicks;
                    opt.WriteTextLogs = writeTextLogs;
                    opt.WriteJsonLines = writeJsonLines;
                });
            });

            // ── Konfigurace ───────────────────────────────────────────────────
            var configProvider = ConfigProvider.Configuration;
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

            // ── IWorldClock — Earth time → World time mapping ─────────────────
            services.AddSingleton<IWorldClock>(sp =>
            {
                var wSpec = sp.GetRequiredService<WorldTimeSpec>();
                long beginningTicks = wSpec.Calendar.DaysFromDate(1, 1, 1) * wSpec.TicksPerDay;

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
                    opt.NPCDirectory = generatedFileOptions.NPCDirectory;
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
                DefaultMemoryEngine,
                DefaultSemanticMemoryEngine,
                DefaultGoalEngine,
                DefaultDailyScheduleEngine>();

            services.AddOptions<MenstrualCycleConfig>()
                    .BindConfiguration("Characters:MenstrualCycle");

            // ── HumanBlueprintSpec — lazy factory ─────────────────────────────
            services.AddCharacterGeneration(sp =>
            {
                var clock = sp.GetRequiredService<IClock>();
                return HumanBlueprintSpec.Default(clock.Now.Date);
            });

            services.AddSingleton<SqliteWorldDatabase>(sp =>
            {
                var db = new SqliteWorldDatabase(FileSystemConstant.SourceFilePath.WorldDatabase);
                // Applies schema.sql (always) and seed_data.sql (when database is empty).
                // Script resolution: disk override → embedded resource fallback.
                WorldDatabaseSeeder.Initialize(db);
                return db;
            });

            services.AddSingleton<ISocialNormProvider, SqliteSocialNormProvider>();

            services.AddSingleton<ILocationService>(sp =>
                new DefaultLocationService(sp.GetRequiredService<ISocialNormProvider>()));
            services.AddSingleton<SqliteWorldObjectProvider>();

            // Write buffer — wraps the provider, buffers mutations
            services.AddSingleton<WorldObjectWriteBuffer>();

            // IMutableWorldObjectProvider → WriteBuffer (instead of the direct provider)
            services.AddSingleton<IMutableWorldObjectProvider>(
                sp => sp.GetRequiredService<WorldObjectWriteBuffer>());

            // The read cache still wraps the direct provider (not the buffer) — it reads committed data
            services.AddSingleton<WorldObjectSnapshotCache>(sp =>
                new WorldObjectSnapshotCache(sp.GetRequiredService<SqliteWorldObjectProvider>()));

            services.AddSingleton<IWorldObjectProvider>(
                sp => sp.GetRequiredService<WorldObjectSnapshotCache>());

            services.AddSingleton<ObjectRespawnScheduler>();

            services.AddObjectInteractionEngine();

            services.AddSingleton<DefaultMovementSpeedProvider>();
            services.AddSingleton<IMovementSpeedProvider>(sp => sp.GetRequiredService<DefaultMovementSpeedProvider>());

            services.AddFamilySystem();

            // ── Manager ───────────────────────────────────────────────────────
            services.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
            services.Configure<GameEngineToolsManagerOptions>(opt =>
            {
                opt.UseConsoleLogging = consoleLogs;
                opt.LogsRoot = logsRoot;
            });

            // ── Building the DI container ─────────────────────────────────────
            var provider = services.BuildServiceProvider();

            // ── WWorld.Configure — ambient konfigurace pro W-typy ─────────────
            var spec = provider.GetRequiredService<WorldTimeSpec>();
            var clock = provider.GetRequiredService<IClock>();
            WWorld.Configure(spec, clock);

            // ── Inicializace manageru ─────────────────────────────────────────
            var manager = provider.GetRequiredService<IGameEngineToolsManager>();
            manager.Initialize();

            return new GameEngineToolsRuntimeHandle(provider);
        }

        #endregion StartAsync
    }

    /// <summary>
    /// A handle to the running runtime. Holds the <see cref="IServiceProvider"/> and exposes
    /// the key services for the game loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements <see cref="IAsyncDisposable"/> — dispose stops the subscribers
    /// and releases the DI container (and all <see cref="IDisposable"/> singletons in it).
    /// </para>
    /// <para>
    /// Recommended usage:
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
        /// The DI provider built in <see cref="GameEngineToolsRuntime.StartAsync"/>.
        /// We store it as a <see cref="ServiceProvider"/> (the concrete type) because
        /// we need <see cref="ServiceProvider.DisposeAsync"/> — <see cref="IServiceProvider"/>
        /// tento interface neimplementuje.
        /// </summary>
        private readonly ServiceProvider _provider;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Internal constructor — called only by <see cref="GameEngineToolsRuntime.StartAsync"/>.
        /// </summary>
        /// <param name="provider">The fully built DI container.</param>
        internal GameEngineToolsRuntimeHandle(ServiceProvider provider)
            => _provider = provider;

        #endregion Konstrukce

        #region Veřejné vlastnosti

        /// <summary>
        /// The game clock — current time, with the ability to pause / change speed.
        /// </summary>
        public IClock Clock
            => _provider.GetRequiredService<IClock>();

        /// <summary>
        /// The main manager of characters and the game world.
        /// </summary>
        public IGameEngineToolsManager GameEngineToolsManager
            => _provider.GetRequiredService<IGameEngineToolsManager>();

        /// <summary>
        /// DI provider for directly resolving any service.
        /// Use sparingly — prefer the typed properties above.
        /// </summary>
        public IServiceProvider Services => _provider;

        #endregion Veřejné vlastnosti

        #region IAsyncDisposable

        /// <summary>
        /// Releases the DI container and all <see cref="IDisposable"/> / <see cref="IAsyncDisposable"/>
        /// singletons registered in it. No hosted services to stop — that was
        /// removed along with <c>IHost</c>.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            _provider.GetService<ICharactersLogControl>()?.FlushAll();
            await _provider.DisposeAsync().ConfigureAwait(false);
        }

        #endregion IAsyncDisposable
    }
}
