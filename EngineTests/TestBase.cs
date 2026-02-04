namespace GameTester
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    using GameEngineTools.Extensions;
    using GameEngineTools.FileSystem;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Core.Calendars;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using GameTester.Config;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    [TestClass]
    public abstract class TestBase
    {
        protected const int MaxHealth = 100;
        protected const int PlayersMaxAge = 35;
        protected const int PlayersMinAge = 15;
        protected List<string> Filenames { get; set; } = new List<string>();
        protected Random Random { get; private set; }

        protected void GetFiles()
        {
            var path = GameEngineTools.Constants.TestFSConstatns.gfiles;
            foreach (var dirs in Directory.GetDirectories(path))
            {
                foreach (var file in Directory.GetFiles(dirs))
                {
                    var f = new FileInfo(file).Name;
                    switch (dirs)
                    {
                        case "Player":
                            Filenames[0] = f;
                            break;

                        case "NPCs":
                            Filenames.Add(f);
                            break;
                    }
                }
            }
        }

        public virtual void Import()
        {
            CharacterManager.Initialize();
            var nppcs = CharacterManager.NPPCs;

            foreach (var filename in Filenames)
            {
                if (filename.Equals(Filenames.First()))
                {
                    var pc = GeneratedFile.ImportPC(filename);
                    nppcs.Add(pc);
                    continue;
                }
                var npc = GeneratedFile.ImportNPC(filename);
                nppcs.Add(npc);
            }

            Assert.IsTrue(nppcs.Count > 0);
            Assert.IsTrue(GameEngineTools.Characters.GameObjects.CharacterBase.People.Count > 0);
        }

        public virtual void Import(out List<GameEngineTools.Characters.GameObjects.CharacterBase> nppcs)
        {
            CharacterManager.Initialize();
            nppcs = new List<GameEngineTools.Characters.GameObjects.CharacterBase>();

            foreach (var filename in Filenames)
            {
                if (filename.Equals(Filenames.First()))
                {
                    var pc = GeneratedFile.ImportPC(filename);
                    nppcs.Add(pc);
                    continue;
                }
                var npc = GeneratedFile.ImportNPC(filename);
                nppcs.Add(npc);
            }

            Assert.IsTrue(nppcs.Count > 0);
            Assert.IsTrue(GameEngineTools.Characters.GameObjects.CharacterBase.People.Count > 0);
        }

        protected virtual void TestInit()
        {
            InitializeServicesAndGetProvider();
            Filenames.Clear();
        }

        protected IServiceProvider ServiceProvider { get; set; }
        public GameEngineToolsManager CharacterManager { get; protected set; }
        protected GeneratedFile GeneratedFile { get; set; }
        protected WorldClock worldClock;
        protected WorldTimeSpec spec;

        protected IWorldClock InitializeTestWorldClock(IOptions<InitWorldClockConfig> options)
        {
            var opts = options.Value;
            var calendar = new FixedMonthsCalendar(opts.DaysInMonths, y => y % opts.LeapYearInterval == 0 ? opts.LeapExtraDays : 0);
            this.spec = new WorldTimeSpec(opts.TicksPerSecond, opts.SecondsPerMinute, opts.MinutesPerHour, opts.HoursPerDay, calendar);
            WDateTime.Use(spec);
            this.worldClock = WorldClock.AlignNow(spec, WDateTime.FromParts(132, 1, 1));
            WDateTime.UseClock(worldClock);
            return this.worldClock;
        }

        [TestInitialize]
        public void Init()
        {
            this.Random = new Random();
            TestInit();
        }

        protected virtual void InitializeServicesAndGetProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging(lb =>
            {
                lb.ClearProviders();
                lb.AddConsole();
                lb.AddCharactersFile(opt =>
                {
                    opt.FilePath = "logs/Characters/characters.log";
                    opt.MinLevel = LogLevel.Information;
                    opt.UseUtcTimestamps = true;
                });
            });

            // Configuration
            var cprovider = Config.ConfigProvider.Configuration;
            var useWorldType = cprovider.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            services.AddSingleton<IConfiguration>(cprovider);
            services.AddOptionsWithValidateOnStart<InitWorldClockConfig>().Configure<IConfiguration>((opt, cfg) =>
            {
                opt.DaysInMonths = Array.Empty<int>();
                cfg.GetSection($"InitWorldClock:{useWorldType}").Bind(opt);
            });

            services.AddSingleton<IWorldClock>(sp => InitializeTestWorldClock(sp.GetRequiredService<IOptions<InitWorldClockConfig>>()));
            services.AddSingleton<IClock, TestClock>();
            services.AddSingleton<IGeneratedFile, GeneratedFile>();
            services.Configure<GeneratedFileOptions>(opt =>
            {
                opt.NPCDirectory = GameEngineTools.Constants.TestFSConstatns.NPCs;
                opt.PlayerDirectory = GameEngineTools.Constants.TestFSConstatns.player;
            });

            services.AddCharacters<
                        DefaultPhysiologyEngine,
                        DefaultPsychologyEngine,
                        DefaultBehaviorEngine,
                        DefaultInteractionEngine,
                        DefaultRelationshipsEngine,
                        DefaultMemoryEngine
                        >();

            services.AddOptions<MenstrualCycleConfig>().BindConfiguration("Characters:MenstrualCycle");

            var now = WDateOnly.FromDateTime(services.BuildServiceProvider().GetRequiredService<IClock>().Now);
            var blueprintSpec = GameEngineTools.Characters.Generation.HumanBlueprintSpec.Default(now);

            services.AddCharacterGeneration(blueprintSpec);

            services.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
            services.Configure<GameEngineToolsManagerOptions>(opt =>
            {
                opt.UseConsoleLogging = true;
            });

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

            Assert.IsNotNull(provider.GetRequiredService<IClock>().Now);

            CharacterManager = (GameEngineToolsManager)provider.GetRequiredService<IGameEngineToolsManager>();

            GeneratedFile = (GeneratedFile)provider.GetRequiredService<IGeneratedFile>();

            ServiceProvider = provider;
        }

        [TestCleanup]
        public void Cleanup()
        {
            CharacterManager.NPPCs.Clear();
            CharacterManager.Items.Clear();
            Filenames.Clear();
            this.Random = null;
            this.worldClock = null;
            this.spec = null;
            WDateTime.UseClock(null);
            WDateTime.Use(null);
        }
    }
}