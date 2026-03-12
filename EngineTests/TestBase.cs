// TestBase.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using EngineTests.Utils;
    using GameEngineTools;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.GameObjects;
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
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Základní třída pro synchronní integrační testy herního enginu.
    /// Sestaví DI kontejner a zpřístupní klíčové služby jako chráněné vlastnosti.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pro asynchronní testy (hosted services) použij <see cref="AsyncTestBase"/>.
    /// </para>
    /// <para>
    /// <b>Pořadí DI závislostí:</b>
    /// <code>
    /// InitWorldClockConfig (appsettings)
    ///   → WorldTimeSpec      (singleton)
    ///   → IWorldClock        (singleton)
    ///   → IClock / TestClock (singleton — bere WorldTimeSpec přímo)
    ///   → WorldTimeContext   (singleton — legacy wrapper)
    ///   → WWorld.Configure   (ambient konfigurace pro W-typy)
    ///   → HumanBlueprintSpec (lazy factory)
    /// </code>
    /// </para>
    /// </remarks>
    [TestClass]
    public abstract class TestBase
    {
        #region Konstanty

        protected const int MaxHealth = 100;
        protected const int PlayersMaxAge = 35;
        protected const int PlayersMinAge = 15;

        #endregion Konstanty

        #region Chráněné vlastnosti

        /// <summary>DI provider sestavený v <see cref="InitializeServicesAndGetProvider"/>.</summary>
        protected IServiceProvider ServiceProvider { get; set; }

        /// <summary>Správce postav.</summary>
        public GameEngineToolsManager CharacterManager { get; protected set; }

        /// <summary>Export/import postav do/ze souborů.</summary>
        protected GeneratedFile GeneratedFile { get; set; }

        /// <summary>Zdroj náhodných čísel pro pomocné výpočty v testech.</summary>
        protected Random Random { get; private set; }

        /// <summary>Seznam názvů souborů pro import/export postav.</summary>
        protected List<string> Filenames { get; set; } = new();

        #endregion Chráněné vlastnosti

        #region TestInitialize / TestCleanup

        /// <summary>
        /// Spouštěno před každým testem — inicializuje Random a sestaví DI.
        /// </summary>
        [TestInitialize]
        public void Init()
        {
            Random = new Random();
            TestInit();
        }

        /// <summary>
        /// Spouštěno po každém testu — uklízí sdílený stav pro izolaci testů.
        /// </summary>
        /// <remarks>
        /// <see cref="WWorld.Reset"/> zajistí, že ambient konfigurace nepřeteče
        /// mezi testy — každý test musí začínat čistým slate.
        /// </remarks>
        [TestCleanup]
        public void Cleanup()
        {
            // Resetujeme WWorld aby ambient konfigurace nepřetékala mezi testy
            WWorld.Reset();

            CharacterManager?.NPPCs.Clear();
            CharacterManager?.Items.Clear();
            Filenames.Clear();
            Random = null;
        }

        /// <summary>
        /// Hook pro odvozené třídy — voláno z <see cref="Init"/>.
        /// Výchozí implementace volá <see cref="InitializeServicesAndGetProvider"/>.
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
        /// Načte soubory postav z testovacího souborového systému do <see cref="Filenames"/>.
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
        /// Importuje všechny postavy ze <see cref="Filenames"/> do <see cref="CharacterManager"/>.
        /// </summary>
        public virtual void Import()
        {
            CharacterManager.Initialize();
            var nppcs = CharacterManager.NPPCs;
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
        /// Importuje všechny postavy a vrátí je jako seznam.
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

        #region DI inicializace

        /// <summary>
        /// Sestaví DI kontejner pro testy, nakonfiguruje <see cref="WWorld"/>
        /// a naplní chráněné vlastnosti.
        /// </summary>
        /// <remarks>
        /// <b>Proč jen jedno <c>BuildServiceProvider()</c>?</b><br/>
        /// Původní kód volal <c>BuildServiceProvider()</c> dvakrát —
        /// každé volání vytvořilo nový kontejner s vlastními singletony.
        /// Nyní je <c>HumanBlueprintSpec</c> lazy factory — vyhodnotí se až
        /// při prvním resolve, kdy DI kontejner existuje a <c>IClock</c> je k dispozici.
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
                    opt.FilePath = "logs/Characters/characters.log";
                    opt.MinLevel = LogLevel.Debug;
                    opt.UseUtcTimestamps = true;
                });
            });

            // ── Konfigurace ───────────────────────────────────────────────────
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

            // ── Soubory ───────────────────────────────────────────────────────
            services.AddSingleton<IGeneratedFile, GeneratedFile>();
            services.Configure<GeneratedFileOptions>(opt =>
            {
                opt.NPCDirectory = GameEngineTools.Constants.TestFSConstatns.NPCs;
                opt.PlayerDirectory = GameEngineTools.Constants.TestFSConstatns.player;
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

            // ── HumanBlueprintSpec — lazy factory ──────────────────────────────
            //    WWorld musí být nakonfigurován před prvním resolve (níže).
            services.AddCharacterGeneration(sp =>
            {
                var clock = sp.GetRequiredService<IClock>();
                return HumanBlueprintSpec.Default(clock.Now.Date);
            });

            // ── Manager ───────────────────────────────────────────────────────
            services.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
            services.Configure<GameEngineToolsManagerOptions>(opt =>
            {
                opt.UseConsoleLogging = true;
            });

            // ── Sestavení — jedno volání, jeden kontejner, sdílené singletony ─
            var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true });

            // ── WWorld.Configure — ambient konfigurace pro W-typy ──────────────
            // Musí proběhnout před jakýmkoli voláním WDateTime.Now, dt.Year atd.
            var resolvedSpec = provider.GetRequiredService<WorldTimeSpec>();
            var resolvedClock = provider.GetRequiredService<IClock>();
            WWorld.Configure(resolvedSpec, resolvedClock);

            CharacterManager = (GameEngineToolsManager)provider.GetRequiredService<IGameEngineToolsManager>();
            GeneratedFile = (GeneratedFile)provider.GetRequiredService<IGeneratedFile>();
            ServiceProvider = provider;

            Assert.IsNotNull(provider.GetRequiredService<IClock>().Now);
        }

        #endregion DI inicializace
    }
}
