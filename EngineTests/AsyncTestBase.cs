// AsyncTestBase.cs
// Copyright (c) 50PSoftware

namespace GameTester
{
    using EngineTests.Utils;
    using GameEngineTools;
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
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Základní třída pro asynchronní integrační testy herního enginu.
    /// Rozšiřuje <see cref="TestBase"/> o spouštění hosted services
    /// (<c>GameEngineToolsManagerInitializer</c>, <c>SubscribersActivator</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Životní cyklus testu:</b>
    /// <code>
    /// [TestInitialize] TestBase.Init()       → InitializeServicesAndGetProvider()
    /// [TestInitialize] InitializeAsync()     → Start hosted services → OnInitAsync()
    /// [TestMethod]     VašTest()
    /// [TestCleanup]    CleanupAsync()        → OnCleanupAsync() → Stop hosted services
    /// [TestCleanup]    TestBase.Cleanup()    → Vyčistí CharacterManager, Filenames…
    /// </code>
    /// </para>
    /// <para>
    /// Hosted services jsou zastavovány v obráceném pořadí (LIFO) pro správné teardown.
    /// </para>
    /// </remarks>
    [TestClass]
    public abstract class AsyncTestBase : TestBase
    {
        #region Soukromá pole

        /// <summary>
        /// Seznam nastartovaných hosted services — slouží pro správné zastavení v Cleanup.
        /// </summary>
        private readonly List<IHostedService> _hostedServices = new();

        #endregion Soukromá pole

        #region DI inicializace (override)

        /// <summary>
        /// Sestaví DI kontejner pro asynchronní testy. Přidává hosted services
        /// (<c>GameEngineToolsManagerInitializer</c>, <c>SubscribersActivator</c>)
        /// oproti synchronní variantě v <see cref="TestBase"/>.
        /// </summary>
        protected override void InitializeServicesAndGetProvider()
        {
            var s = new ServiceCollection();

            // ── Logging ───────────────────────────────────────────────────────
            s.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddConsole();
                lb.AddCharactersFile(opt =>
                {
                    opt.FilePath = "logs/Characters/characters.log";
                    opt.MinLevel = LogLevel.Debug;
                    opt.UseUtcTimestamps = true;
                });
            });

            // ── Konfigurace ───────────────────────────────────────────────────
            var configProvider = Config.ConfigProvider.Configuration;
            var worldTypeConfig = configProvider.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            s.AddSingleton<IConfiguration>(configProvider);

            s.AddOptionsWithValidateOnStart<InitWorldClockConfig>()
             .Configure<IConfiguration>((opt, cfg) =>
             {
                 opt.DaysInMonths = Array.Empty<int>();
                 cfg.GetSection($"InitWorldClock:{worldTypeConfig}").Bind(opt);
             });

            // ── WorldTimeSpec — singleton ─────────────────────────────────────
            //    Opravený bug z původního AsyncTestBase: WorldTimeContext byl
            //    zaregistrován, ale WorldTimeSpec NE → DI by selhal při resolve
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

            // ── IWorldClock — kotva rok 132, 1. den ───────────────────────────
            s.AddSingleton<IWorldClock>(sp =>
            {
                var wSpec = sp.GetRequiredService<WorldTimeSpec>();
                long beginningTicks = wSpec.Calendar.DaysFromDate(132, 1, 1) * wSpec.TicksPerDay;
                return WorldClock.AlignNow(wSpec, beginningTicks);
            });

            // ── TestClock ─────────────────────────────────────────────────────
            s.AddSingleton<IClock, TestClock>();

            // ── Soubory ───────────────────────────────────────────────────────
            s.AddSingleton<IGeneratedFile, GeneratedFile>();
            s.Configure<GeneratedFileOptions>(opt =>
            {
                opt.NPCDirectory = GameEngineTools.Constants.TestFSConstatns.NPCs;
                opt.PlayerDirectory = GameEngineTools.Constants.TestFSConstatns.player;
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
            s.AddCharacterGeneration(sp =>
            {
                var clock = sp.GetRequiredService<IClock>();
                return HumanBlueprintSpec.Default(clock.Now.Date);
            });

            // ── Manager + hosted services ─────────────────────────────────────
            //    Na rozdíl od TestBase zde registrujeme hosted services,
            //    které spouštíme ručně v InitializeAsync()
            s.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
            s.Configure<GameEngineToolsManagerOptions>(opt =>
            {
                opt.UseConsoleLogging = true;
            });

            // ── Sestavení ─────────────────────────────────────────────────────
            var provider = s.BuildServiceProvider();

            var resolvedSpec = provider.GetRequiredService<WorldTimeSpec>();
            var resolvedClock = provider.GetRequiredService<IClock>();
            WWorld.Configure(resolvedSpec, resolvedClock);

            CharacterManager = (GameEngineToolsManager)provider.GetRequiredService<IGameEngineToolsManager>();
            GeneratedFile = (GeneratedFile)provider.GetRequiredService<IGeneratedFile>();
            ServiceProvider = provider;

            Assert.IsNotNull(provider.GetRequiredService<IClock>().Now);
        }

        #endregion DI inicializace (override)

        #region Async lifecycle hooks

        /// <summary>
        /// Hook pro vlastní async přípravu dat — volá se po startu hosted services.
        /// Přepiš v odvozené třídě pro seed dat, naplnění databáze atp.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        protected virtual Task OnInitAsync(CancellationToken ct) => Task.CompletedTask;

        /// <summary>
        /// Hook pro vlastní async úklid — volá se těsně před zastavením hosted services.
        /// Přepiš v odvozené třídě pro rollback dat, asserty nad finálním stavem atp.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        protected virtual Task OnCleanupAsync(CancellationToken ct) => Task.CompletedTask;

        #endregion Async lifecycle hooks

        #region TestInitialize / TestCleanup (async)

        /// <summary>
        /// Spouštěno po <see cref="TestBase.Init"/> — nastartuje hosted services
        /// a zavolá <see cref="OnInitAsync"/>.
        /// </summary>
        [TestInitialize]
        public async Task InitializeAsync()
        {
            foreach (var h in ServiceProvider.GetServices<IHostedService>())
            {
                await h.StartAsync(CancellationToken.None).ConfigureAwait(false);
                _hostedServices.Add(h);
            }

            await OnInitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Spouštěno před <see cref="TestBase.Cleanup"/> — zavolá <see cref="OnCleanupAsync"/>
        /// a zastaví hosted services v obráceném pořadí (LIFO).
        /// </summary>
        [TestCleanup]
        public async Task CleanupAsync()
        {
            try
            {
                await OnCleanupAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                // Zastavujeme v obráceném pořadí — co se nastartovalo poslední, zastaví se první
                for (int i = _hostedServices.Count - 1; i >= 0; i--)
                {
                    await _hostedServices[i].StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            _hostedServices.Clear();
        }

        #endregion TestInitialize / TestCleanup (async)
    }
}
