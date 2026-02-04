namespace GameTester
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using EngineTests.Utils;
    using GameEngineTools;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Config;
    using GameEngineTools.FileSystem;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using GameTester.Extensions;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    [TestClass]
    public abstract class AsyncTestBase : TestBase
    {

        protected override void InitializeServicesAndGetProvider()
        {
            var s = new ServiceCollection();
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
            var configProvider = Config.ConfigProvider.Configuration;
            s.AddSingleton<IConfiguration>(configProvider);
            var worldTypeConfig = configProvider.GetSection("InitWorldClock").GetValue<string>("UseWorldType");
            s.AddOptionsWithValidateOnStart<InitWorldClockConfig>().Configure<IConfiguration>((opt, cfg) =>
            {
                opt.DaysInMonths = Array.Empty<int>();
                cfg.GetSection($"InitWorldClock:{worldTypeConfig}").Bind(opt);
            });

            s.AddSingleton<IWorldClock>(sp => InitializeTestWorldClock(sp.GetRequiredService<IOptions<InitWorldClockConfig>>()));
            s.AddSingleton<IClock, TestClock>();
            s.AddSingleton<IGeneratedFile, GeneratedFile>();
            s.Configure<GeneratedFileOptions>(opt =>
            {
                opt.NPCDirectory = GameEngineTools.Constants.TestFSConstatns.NPCs;
                opt.PlayerDirectory = GameEngineTools.Constants.TestFSConstatns.player;
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

            var now = WDateOnly.FromDateTime(s.BuildServiceProvider().GetRequiredService<IClock>().Now);
            var blueprintSpec = GameEngineTools.Characters.Generation.HumanBlueprintSpec.Default(now);

            s.AddCharacterGeneration(blueprintSpec);

            s.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
            s.Configure<GameEngineToolsManagerOptions>(opt =>
            {
                opt.UseConsoleLogging = true;
            });
            s.AddHostedService<GameEngineToolsManagerInitializer>();

            s.AddHostedService<SubscribersActivator>();

            ServiceProvider = s.BuildServiceProvider();
            CharacterManager = (GameEngineToolsManager)ServiceProvider.GetRequiredService<IGameEngineToolsManager>();
            GeneratedFile = (GeneratedFile)ServiceProvider.GetRequiredService<IGeneratedFile>();

            Assert.IsNotNull(ServiceProvider.GetRequiredService<IClock>().Now);
        }


        private readonly List<IHostedService> hostedServices = new();

        /// <summary>
        /// Hook na vlastní async přípravu dat – volá se po startu hosted služeb.
        /// </summary>
        protected virtual Task OnInitAsync(CancellationToken ct) => Task.CompletedTask;

        /// <summary>
        /// Hook na vlastní async úklid – volá se před stopem hosted služeb.
        /// </summary>
        protected virtual Task OnCleanupAsync(CancellationToken ct) => Task.CompletedTask;

        [TestInitialize]
        public async Task InitializeAsync()
        {
            foreach (var h in ServiceProvider.GetServices<IHostedService>())
            {
                await h.StartAsync(CancellationToken.None).ConfigureAwait(false);
                hostedServices.Add(h);
            }

            await OnInitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [TestCleanup]
        public async Task CleanupAsync()
        {
            try
            {
                await OnCleanupAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                for (int idx = hostedServices.Count - 1; idx >= 0; idx--)
                {
                    await hostedServices[idx].StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            hostedServices.Clear();
        }
    }
}
