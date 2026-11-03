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
