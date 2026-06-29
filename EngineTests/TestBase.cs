// TestBase.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using EngineTests.Utils;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Config;
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
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Base class for synchronous integration tests of the game engine.
    /// Builds the DI container and exposes key services as protected properties.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For asynchronous tests (hosted services) use <see cref="AsyncTestBase"/>.
    /// </para>
    /// <para>
    /// <b>DI dependency order:</b>
    /// <code>
    /// InitWorldClockConfig (appsettings)
    ///   → WorldTimeSpec      (singleton)
    ///   → IWorldClock        (singleton)
    ///   → IClock / TestClock (singleton — takes WorldTimeSpec directly)
    ///   → WorldTimeContext   (singleton — legacy wrapper)
    ///   → WWorld.Configure   (ambient configuration for W-types)
    ///   → HumanBlueprintSpec (lazy factory)
    /// </code>
    /// </para>
    /// </remarks>
    [TestClass]
    public abstract class TestBase
    {
        #region Constants

        protected const int MaxHealth = 100;
        protected const int PlayersMaxAge = 35;
        protected const int PlayersMinAge = 15;

        #endregion Constants

        #region Protected properties

        /// <summary>DI provider built in <see cref="InitializeServicesAndGetProvider"/>.</summary>
        protected IServiceProvider ServiceProvider { get; set; }

        /// <summary>Character manager.</summary>
        public GameEngineToolsManager CharacterManager { get; protected set; }

        /// <summary>Export/import of characters to/from files.</summary>
        protected GeneratedFile GeneratedFile { get; set; }

        /// <summary>Random-number source for helper computations in tests.</summary>
        protected Random Random { get; private set; }

        /// <summary>List of file names for character import/export.</summary>
        protected List<string> Filenames { get; set; } = new();

        #endregion Protected properties

        #region TestInitialize / TestCleanup

        /// <summary>
        /// Run before each test — initializes Random and builds DI.
        /// </summary>
        [TestInitialize]
        public void Init()
        {
            Random = new Random();
            TestInit();
        }

        /// <summary>
        /// Run after each test — cleans up shared state for test isolation.
        /// </summary>
        /// <remarks>
        /// <see cref="WWorld.Reset"/> ensures that ambient configuration does not leak
        /// between tests — every test must start from a clean slate.
        /// </remarks>
        [TestCleanup]
        public void Cleanup()
        {
            // Reset WWorld so ambient configuration does not leak between tests
            WWorld.Reset();

            CharacterManager?.Characters.Clear();
            CharacterManager?.Items.Clear();
            Filenames.Clear();
            Random = null;
        }

        /// <summary>
        /// Hook for derived classes — called from <see cref="Init"/>.
        /// The default implementation calls <see cref="InitializeServicesAndGetProvider"/>.
        /// </summary>
        protected virtual void TestInit()
        {
            InitializeServicesAndGetProvider();
            var testClock = (TestClock)ServiceProvider.GetRequiredService<IClock>();
            testClock.SetNow(WDateTime.New(WDateOnly.New(100, 1, 1)));
            Filenames.Clear();
        }

        #endregion TestInitialize / TestCleanup

        #region Import / Export

        /// <summary>
        /// Loads character files from the test file system into <see cref="Filenames"/>.
        /// </summary>
        protected void GetFiles()
        {
            var path = GameEngineTools.Constants.TestFSConstatns.gfiles;
            foreach (var dir in Directory.GetDirectories(path))
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    var f = new FileInfo(file).Name;
                    switch (dir)
                    {
                        case "Player": Filenames[0] = f; break;
                        case "NPCs": Filenames.Add(f); break;
                    }
                }
            }
        }

        /// <summary>
        /// Imports all characters from <see cref="Filenames"/> into <see cref="CharacterManager"/>.
        /// </summary>
        public virtual void Import()
        {
            CharacterManager.Initialize();
            var nppcs = CharacterManager.Characters;
            foreach (var filename in Filenames)
            {
                if (filename.Equals(Filenames.First()))
                {
                    nppcs.Add(GeneratedFile.ImportPC(filename));
                    continue;
                }
                nppcs.Add(GeneratedFile.ImportNPC(filename));
            }
            Assert.IsTrue(nppcs.Count > 0);
        }

        /// <summary>
        /// Imports all characters and returns them as a list.
        /// </summary>
        public virtual void Import(out List<CharacterBase> nppcs)
        {
            CharacterManager.Initialize();
            nppcs = new List<CharacterBase>();
            foreach (var filename in Filenames)
            {
                if (filename.Equals(Filenames.First()))
                {
                    nppcs.Add(GeneratedFile.ImportPC(filename));
                    continue;
                }
                nppcs.Add(GeneratedFile.ImportNPC(filename));
            }
            Assert.IsTrue(nppcs.Count > 0);
        }

        #endregion Import / Export

        #region DI initialization

        /// <summary>
        /// Builds the DI container for tests, configures <see cref="WWorld"/>
        /// and populates the protected properties.
        /// </summary>
        /// <remarks>
        /// <b>Why only one <c>BuildServiceProvider()</c>?</b><br/>
        /// The original code called <c>BuildServiceProvider()</c> twice —
        /// each call created a new container with its own singletons.
        /// Now <c>HumanBlueprintSpec</c> is a lazy factory — it is evaluated only
        /// on the first resolve, when the DI container exists and <c>IClock</c> is available.
        /// </remarks>
        protected virtual void InitializeServicesAndGetProvider()
        {
            var services = new ServiceCollection();

            // ── Logging ───────────────────────────────────────────────────────
            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddConsole();
                lb.AddCharactersFile(opt =>
                {
                    opt.LogsDirectoryPath = Path.Combine("logs", "Characters");
                    opt.MinLevel = LogLevel.Debug;
                    opt.UseUtcTimestamps = true;
                });
            });

            // ── Configuration ─────────────────────────────────────────────────
            var cprovider = Config.ConfigProvider.Configuration;
            var useWorldType = cprovider.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            services.AddSingleton<IConfiguration>(cprovider);

            services.AddOptionsWithValidateOnStart<InitWorldClockConfig>()
                    .Configure<IConfiguration>((opt, cfg) =>
                    {
                        opt.DaysInMonths = Array.Empty<int>();
                        cfg.GetSection($"InitWorldClock:{useWorldType}").Bind(opt);
                    });

            // ── WorldTimeSpec — singleton ──────────────────────────────────────
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

            services.AddSingleton<IWorldClock>(sp =>
            {
                var wSpec = sp.GetRequiredService<WorldTimeSpec>();
                long beginningTicks = wSpec.Calendar.DaysFromDate(1, 1, 1) * wSpec.TicksPerDay;
                return WorldClock.AlignNow(wSpec, beginningTicks);
            });

            services.AddSingleton<IClock, TestClock>();

            // ── Files ─────────────────────────────────────────────────────────
            services.AddSingleton<IGeneratedFile, GeneratedFile>();
            services.Configure<GeneratedFileOptions>(opt =>
            {
                opt.NPCDirectory = GameEngineTools.Constants.TestFSConstatns.NPCs;
                opt.PlayerDirectory = GameEngineTools.Constants.TestFSConstatns.player;
            });

            // ── Character engines ─────────────────────────────────────────────
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

            // ── HumanBlueprintSpec — lazy factory ──────────────────────────────
            //    WWorld must be configured before the first resolve (below).
            services.AddCharacterGeneration(sp =>
            {
                var clock = sp.GetRequiredService<IClock>();
                return HumanBlueprintSpec.Default(clock.Now.Date);
            });

            services.AddSingleton<SqliteWorldDatabase>(_ =>
            {
                var db = new SqliteWorldDatabase(":memory:");
                // Schema must be applied explicitly for in-memory databases —
                // production databases get this via GameEngineToolsRuntime.
                WorldDatabaseSeeder.Initialize(db);
                return db;
            });

            services.AddSingleton<ISocialNormProvider, SqliteSocialNormProvider>();

            services.AddSingleton<ILocationService>(sp =>
                new DefaultLocationService(sp.GetRequiredService<ISocialNormProvider>()));
            services.AddSingleton<SqliteWorldObjectProvider>();
            //services.AddSingleton<IMutableWorldObjectProvider>(
            //    sp => sp.GetRequiredService<SqliteWorldObjectProvider>());
            //services.AddSingleton<IWorldObjectProvider>(
            //    sp => sp.GetRequiredService<SqliteWorldObjectProvider>());

            // Write buffer — wraps the provider, buffers mutations
            services.AddSingleton<WorldObjectWriteBuffer>();

            // IMutableWorldObjectProvider → WriteBuffer (instead of the direct provider)
            services.AddSingleton<IMutableWorldObjectProvider>(
                sp => sp.GetRequiredService<WorldObjectWriteBuffer>());

            // Read cache still wraps the direct provider (not the buffer) — reads committed data
            services.AddSingleton<WorldObjectSnapshotCache>(sp =>
                new WorldObjectSnapshotCache(sp.GetRequiredService<SqliteWorldObjectProvider>()));

            services.AddSingleton<IWorldObjectProvider>(
                sp => sp.GetRequiredService<WorldObjectSnapshotCache>());

            services.AddSingleton<ObjectRespawnScheduler>();

            services.AddObjectInteractionEngine();

            services.AddOptions<MovementConfig>()
                    .BindConfiguration("World:Movement");
            services.AddSingleton<DefaultMovementSpeedProvider>();
            services.AddSingleton<IMovementSpeedProvider>(sp => sp.GetRequiredService<DefaultMovementSpeedProvider>());

            services.AddFamilySystem();

            // ── Manager ───────────────────────────────────────────────────────
            services.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
            services.Configure<GameEngineToolsManagerOptions>(opt =>
            {
                opt.UseConsoleLogging = true;
            });

            // ── Build — one call, one container, shared singletons ────────────
            var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true });

            // ── WWorld.Configure — ambient configuration for W-types ───────────
            // Must run before any call to WDateTime.Now, dt.Year, etc.
            var resolvedSpec = provider.GetRequiredService<WorldTimeSpec>();
            var resolvedClock = provider.GetRequiredService<IClock>();
            WWorld.Configure(resolvedSpec, resolvedClock);

            CharacterManager = (GameEngineToolsManager)provider.GetRequiredService<IGameEngineToolsManager>();
            GeneratedFile = (GeneratedFile)provider.GetRequiredService<IGeneratedFile>();
            ServiceProvider = provider;

            Assert.IsNotNull(provider.GetRequiredService<IClock>().Now);
        }

        #endregion DI initialization

        #region Helper methods

        /// <summary>RNG that always returns 0 — eliminates random noise from Tick().</summary>
        protected sealed class ZeroRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => false;
        }

        protected sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event)
            { }

            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();
        }

        protected sealed class NullDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }

        protected sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public bool Cancel(ScheduledId id) => true;

            public IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime now)
                => Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }

        protected sealed class FixedSocialFidelityPolicy : ISocialFidelityPolicy
        {
            private readonly SocialFidelityLevel _level;

            public FixedSocialFidelityPolicy(SocialFidelityLevel level)
            {
                _level = level;
            }

            public SocialFidelityLevel GetLevel(HumanId human) => _level;
        }

        #endregion Helper methods
    }
}
