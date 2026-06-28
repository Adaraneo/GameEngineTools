// SimulationSceneAddCharacterTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System.Collections.Generic;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Verifies that <see cref="SimulationScene.AddCharacter"/> lets a character join a running
    /// simulation (e.g. a newborn after a <c>ChildBorn</c> event) and start ticking.
    /// </summary>
    [TestClass]
    public sealed class SimulationSceneAddCharacterTests : TestBase
    {
        /// <summary>
        /// A character added from within <c>OnTick</c> must appear in the live per-tick iteration
        /// on a later substep — proving the scene no longer uses a closed character list.
        /// </summary>
        [TestMethod]
        public void AddCharacter_MidRun_NewcomerJoinsLiveTickIteration()
        {
            // Arrange — the scene requires the concrete SystemClock; build one at the test era.
            var worldClock = ServiceProvider.GetRequiredService<IWorldClock>();
            var spec = ServiceProvider.GetRequiredService<WorldTimeSpec>();
            var clock = new SystemClock(worldClock, spec);
            clock.SetNow(WDateTime.New(WDateOnly.New(100, 1, 1)));

            var lodRuntime = ServiceProvider.GetRequiredService<ICognitiveResolutionLevelRuntime>();

            var first = CharacterManager.RandomizePerson(maxAge: 35, sexBiology: null, minAge: 18);
            var newcomer = CharacterManager.RandomizePerson(maxAge: 35, sexBiology: null, minAge: 18);

            SimulationScene? scene = null;
            var added = false;
            var seen = new HashSet<HumanId>();

            var options = new SimulationSceneOptions
            {
                Characters = new List<IHuman> { first },
                SimulationDays = 1,
                TickStep = WTimeSpan.FromHours(6),
                OnTick = (now, chars) =>
                {
                    foreach (var c in chars)
                        seen.Add(c.Id);

                    // Queue the newcomer on the first tick only.
                    if (!added)
                    {
                        scene!.AddCharacter(newcomer);
                        added = true;
                    }
                },
            };

            scene = new SimulationScene(clock, options, lodRuntime);

            // Act
            scene.RunAsync().GetAwaiter().GetResult();

            // Assert
            Assert.IsTrue(seen.Contains(first.Id), "The initial character should tick.");
            Assert.IsTrue(
                seen.Contains(newcomer.Id),
                "A character added mid-run should join the live per-tick iteration.");
        }
    }
}
