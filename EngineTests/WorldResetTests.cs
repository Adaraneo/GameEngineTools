// WorldResetTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>Resetting the world objects to the generated file, and the scene's reset by scope.</summary>
    [TestClass]
    public sealed class WorldResetTests : TestBase
    {
        private static readonly WDateTime Start = WDateTime.New(WDateOnly.New(100, 1, 1));
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "worldreset-" + Guid.NewGuid().ToString("N"));

        [TestInitialize]
        public void MakeDir() => Directory.CreateDirectory(_dir);

        [TestCleanup]
        public void RemoveDir()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        private static WorldObject Bread(string id, string location) => new()
        {
            Id = id,
            DisplayName = "Bread",
            Category = WorldObjectCategory.Food,
            LocationId = location,
            IsAvailable = true,
            Affordances = [new WorldObjectAffordance(AffordanceType.Hunger, 0.8)],
        };

        [TestMethod]
        public void BaselinePath_PutsGenBeforeTheExtension()
        {
            Assert.AreEqual(Path.Combine("a", "world.gen.db"), WorldBaselineFiles.PathFor(Path.Combine("a", "world.db")));
            Assert.AreEqual("w.gen.db", WorldBaselineFiles.PathFor("w.db"));
        }

        [TestMethod]
        public void Reset_ReturnsTheObjectsToTheGeneratedFile_AndDropsBufferedWrites()
        {
            var worldPath = Path.Combine(_dir, "world.db");
            var genPath = WorldBaselineFiles.PathFor(worldPath);
            using (var gen = new SqliteWorldDatabase(genPath))
            {
                WorldDatabaseSeeder.Initialize(gen);
                gen.ExecuteScript("INSERT INTO Locations (Id, DisplayName, Type) VALUES ('home', 'Home', 'Social');");
                gen.AddObject(Bread("a", "home"));
            }

            WorldBaselineFiles.Publish(genPath, worldPath);

            using var db = new SqliteWorldDatabase(worldPath);
            WorldDatabaseSeeder.Initialize(db);
            var provider = new SqliteWorldObjectProvider(db);
            var buffer = new WorldObjectWriteBuffer(provider, db);
            var cache = new WorldObjectSnapshotCache(buffer);
            var baseline = new SqliteWorldBaseline(db, genPath, buffer, cache);
            var hooked = 0;
            baseline.AddResetHook(() => hooked++);

            Assert.IsNotNull(db.FindObject("a"), "The working copy starts as the generated world.");

            buffer.ConsumeObject("home", "a", Start);
            buffer.Flush();
            buffer.AddObject(Bread("b", "home"));
            buffer.ConsumeObject("home", "b", Start);
            Assert.IsNotNull(db.FindObject("b"));
            Assert.IsNotNull(db.GetAllObjectsAt("home").Single(o => o.Id == "a").ConsumedAt, "The play consumed the bread.");

            baseline.Reset();

            Assert.IsNull(db.FindObject("b"), "What the run added is gone.");
            Assert.IsNull(db.GetAllObjectsAt("home").Single(o => o.Id == "a").ConsumedAt, "What the run consumed is back.");
            Assert.AreEqual(0, buffer.PendingCount, "No buffered write survives the reset.");
            Assert.AreEqual(1, hooked, "Host hooks run.");
        }

        [TestMethod]
        public void Reset_WithoutAGeneratedFile_SaysSo()
        {
            using var db = new SqliteWorldDatabase(Path.Combine(_dir, "world.db"));
            var baseline = new SqliteWorldBaseline(db, Path.Combine(_dir, "nothing.gen.db"));

            Assert.IsFalse(baseline.Exists);
            Assert.ThrowsExactly<FileNotFoundException>(() => baseline.Reset());
        }

        private SimulationScene Scene(IHuman person, IWorldBaseline? baseline, Action<SimulationScene, int>? onTick, Action<WDateTime, IReadOnlyList<IHuman>>? onReset, out SystemClock clock)
        {
            clock = new SystemClock(ServiceProvider.GetRequiredService<IWorldClock>(), ServiceProvider.GetRequiredService<WorldTimeSpec>());
            clock.SetNow(Start);
            SimulationScene? scene = null;
            var ticks = 0;
            var options = new SimulationSceneOptions
            {
                Characters = new List<IHuman> { person },
                SimulationDays = 2,
                TickStep = WTimeSpan.FromHours(1),
                WorldBaseline = baseline,
                OnTick = (_, _) => onTick?.Invoke(scene!, ++ticks),
                OnCharactersReset = onReset,
            };
            scene = new SimulationScene(clock, options, ServiceProvider.GetRequiredService<ICognitiveResolutionLevelRuntime>());
            return scene;
        }

        [TestMethod]
        public void SceneReset_RefusesAClockOnlyResetAndAWorldResetWithoutABaseline_UnlessAllowed()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 35, sexBiology: null, minAge: 18);
            var scene = Scene(person, baseline: null, onTick: null, onReset: null, out _);

            Assert.ThrowsExactly<ArgumentException>(() => scene.Reset(WorldResetScope.World));
            Assert.ThrowsExactly<ArgumentException>(() => scene.Reset(WorldResetScope.Time));
            Assert.ThrowsExactly<ArgumentException>(() => scene.Reset(WorldResetScope.Time | WorldResetScope.Characters));
            scene.Reset(WorldResetScope.Time, allowInconsistentTime: true);
        }

        [TestMethod]
        public void SceneReset_All_PutsTheClockBackAndResetsWorldAndCharacters_InThatOrder()
        {
            var worldPath = Path.Combine(_dir, "world.db");
            var genPath = WorldBaselineFiles.PathFor(worldPath);
            using (var gen = new SqliteWorldDatabase(genPath))
            {
                WorldDatabaseSeeder.Initialize(gen);
                gen.ExecuteScript("INSERT INTO Locations (Id, DisplayName, Type) VALUES ('home', 'Home', 'Social');");
                gen.AddObject(Bread("a", "home"));
            }

            WorldBaselineFiles.Publish(genPath, worldPath);
            using var db = new SqliteWorldDatabase(worldPath);
            WorldDatabaseSeeder.Initialize(db);
            var baseline = new SqliteWorldBaseline(db, genPath);

            var person = CharacterManager.RandomizePerson(maxAge: 35, sexBiology: null, minAge: 18);
            var initial = person.Snapshot;
            WDateTime? clockAtReset = null;
            bool? objectBackAtReset = null;
            var told = 0;
            var consumed = false;
            var diffs = new List<string>();

            var scene = Scene(person, baseline,
                onTick: (s, n) =>
                {
                    if (n == 1)
                    {
                        db.ConsumeObject("home", "a", Start);
                        consumed = db.GetAllObjectsAt("home").Single(o => o.Id == "a").ConsumedAt is not null;
                    }
                    if (n == 30) s.Reset(WorldResetScope.All);
                },
                onReset: (now, chars) =>
                {
                    told++;
                    EngineTests.Utils.DeepCompare.Compare("Snapshot", initial, chars[0].Snapshot, diffs);
                    clockAtReset = now;
                    objectBackAtReset = db.GetAllObjectsAt("home").Single(o => o.Id == "a").ConsumedAt is null;
                },
                out _);

            scene.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(consumed, "The scenario consumed the bread before the reset.");
            Assert.AreEqual(1, told);
            Assert.AreEqual(Start, clockAtReset, "The clock is back at the start of the scene.");
            Assert.IsTrue(objectBackAtReset, "The consumed bread is back.");
            Assert.AreEqual(0, diffs.Count, string.Join("; ", diffs.Take(5)));
        }
    }
}
