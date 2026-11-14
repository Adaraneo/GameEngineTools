// SceneHardeningTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Guards on <see cref="SimulationScene"/> configuration and cancellation.</summary>
    [TestClass]
    public sealed class SceneHardeningTests : TestBase
    {
        private SimulationScene BuildScene(WTimeSpan tickStep)
        {
            var clock = new SystemClock(ServiceProvider.GetRequiredService<IWorldClock>(), ServiceProvider.GetRequiredService<WorldTimeSpec>());
            clock.SetNow(WDateTime.New(WDateOnly.New(100, 1, 1)));
            var options = new SimulationSceneOptions
            {
                Characters = new List<IHuman> { CharacterManager.RandomizePerson(maxAge: 35, sexBiology: null, minAge: 18) },
                SimulationDays = 2,
                TickStep = tickStep,
            };
            return new SimulationScene(clock, options, ServiceProvider.GetRequiredService<ICognitiveResolutionLevelRuntime>());
        }

        private List<double> AirThroughOneDay(GameEngineTools.World.Weather.WeatherConfig? weather)
        {
            var clock = new SystemClock(ServiceProvider.GetRequiredService<IWorldClock>(), ServiceProvider.GetRequiredService<WorldTimeSpec>());
            clock.SetNow(WDateTime.New(WDateOnly.New(100, 4, 1)));
            var person = CharacterManager.RandomizePerson(maxAge: 35, sexBiology: null, minAge: 18);
            var air = new List<double>();
            var options = new SimulationSceneOptions
            {
                Characters = new List<IHuman> { person },
                AstroConfig = new GameEngineTools.World.Core.Astro.AstroConfig(Sun: new GameEngineTools.World.Core.Astro.SunParamsConfig(), LatitudeDeg: 50.0, LongitudeDeg: 0.0, BaseTemperatureCelsius: 11.0, SeasonalAmplitudeCelsius: 9.0, VernalPhase: 0.0),
                Weather = weather,
                WeatherSeed = 3,
                SimulationDays = 1,
                TickStep = WTimeSpan.FromHours(1),
                OnTick = (_, chars) => air.Add(chars[0].Snapshot.AmbientTemperature),
            };
            new SimulationScene(clock, options, ServiceProvider.GetRequiredService<ICognitiveResolutionLevelRuntime>()).RunAsync().GetAwaiter().GetResult();
            return air;
        }

        [TestMethod]
        public void GeneratedWeather_GivesADayAndNightSwing_AndOffKeepsThePlainSeasonalCurve()
        {
            var off = AirThroughOneDay(null);
            var on = AirThroughOneDay(new GameEngineTools.World.Weather.WeatherConfig { Enabled = true });

            Assert.IsTrue(off.Max() - off.Min() < 0.5, "Without weather the air only follows the season.");
            Assert.IsTrue(on.Max() - on.Min() > 4.0, "With weather the day is warmer than the night (the wet-day range is 6 degC, the dry 10).");
        }

        [TestMethod]
        public void NonPositiveTickStep_IsRejected_InsteadOfLoopingForever()
        {
            Assert.Throws<ArgumentException>(() => BuildScene(WTimeSpan.Zero));
        }

        [TestMethod]
        public void RunAsync_StopsWhenCanceled()
        {
            var scene = BuildScene(WTimeSpan.FromHours(0.5));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = scene.RunAsync(cts.Token);

            Assert.IsTrue(task.IsCanceled, "A canceled token ends the run at the next substep boundary.");
        }
    }
}
