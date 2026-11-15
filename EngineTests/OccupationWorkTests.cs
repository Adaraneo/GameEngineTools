// OccupationWorkTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>Occupations as lists of the tasks of tasks.json (occupations.json): what a job does is data, not a set of occupation names in code.</summary>
    [TestClass]
    public sealed class OccupationWorkTests : TestBase
    {
        private const string Loc = "test"; // BehaviorComponentTestFactory surface location

        private const string Materials = """{ "materials": [ { "id": "fired_clay", "class": "Clay", "densityKgM3": 1900 } ] }""";

        private const string Types = """
            { "types": [
              { "id": "kiln", "name": "Pec", "category": "Ambient", "pickable": false, "weightGrams": 500000, "affordances": { "Production": 1.0 } },
              { "id": "clay_lump", "category": "Ambient", "pickable": true, "parts": [ { "material": "fired_clay", "volumeCm3": 500 } ] },
              { "id": "pot", "name": "Hrnec", "category": "Tool", "pickable": true, "parts": [ { "material": "fired_clay", "volumeCm3": 800 } ] } ] }
            """;

        private const string Tasks = """
            { "tasks": [ { "id": "make_pot", "name": "Vypálit hrnec", "kind": "Craft", "inputs": [ { "item": "clay_lump", "qty": 2 } ],
                           "output": { "item": "pot", "qty": 1 }, "durationMinutes": 120, "station": "kiln" } ] }
            """;

        [TestMethod]
        public void TheBuiltInOccupations_ListOnlyTasksThatExist_AndTheFoodChainStaysWithFarmerAndCraftsperson()
        {
            var catalog = WorkCatalogLoader.Load();

            Assert.IsTrue(catalog.OccupationDoesKind(OccupationIds.Farmer, TaskKind.FieldWork));
            Assert.IsTrue(catalog.OccupationDoesKind(OccupationIds.Craftsperson, TaskKind.Craft));
            Assert.IsTrue(catalog.OccupationDoesAction("FARMER", "Reap"), "Occupation ids are compared ignoring case, as in the schedule.");
            Assert.IsFalse(catalog.OccupationDoesKind(OccupationIds.Healer, TaskKind.Craft), "An occupation that lists nothing does no task.");
            Assert.IsNull(catalog.Occupation(OccupationIds.None));
        }

        [TestMethod]
        public void BadOccupations_AreRejected_NamingTheOccupation()
        {
            var unknown = Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, Types, Tasks,
                """{ "occupations": [ { "id": "potter", "tasks": [ "make_vase" ] } ] }"""));
            StringAssert.Contains(unknown.Message, "potter");
            StringAssert.Contains(unknown.Message, "make_vase");

            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, Types, Tasks, """{ "occupations": [ { "id": "potter", "tasks": [] } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, Types, Tasks, """{ "occupations": [ { "id": "potter", "tasks": [ "make_pot", "make_pot" ] } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, Types, Tasks,
                """{ "occupations": [ { "id": "potter", "tasks": [ "make_pot" ] }, { "id": "POTTER", "tasks": [ "make_pot" ] } ] }"""));
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, Types, Tasks, """{ "occupations": [ { "id": "potter" } ] }"""));
        }

        [TestMethod]
        public void APotter_AddedByDataOnly_ProcessesClayAtTheKilnOnShift_AndANonPotterDoesNot()
        {
            var catalog = WorkCatalogLoader.Parse(Materials, Types, Tasks, """{ "occupations": [ { "id": "potter", "tasks": [ "make_pot" ] } ] }""");
            var engine = new ContingencySearchEngine(catalog);
            var kiln = WorkObjects.Spawn(catalog, "kiln", "kiln_1", Loc);
            var clay = new List<WorldObject>
            {
                WorkObjects.Spawn(catalog, "clay_lump", "lump_1", Loc) with { IsPickable = true },
                WorkObjects.Spawn(catalog, "clay_lump", "lump_2", Loc) with { IsPickable = true },
            };

            var potter = engine.Evaluate(ShiftContext("potter", new List<WorldObject> { kiln }.Concat(clay).ToList()));
            var farmer = engine.Evaluate(ShiftContext(OccupationIds.Farmer, new List<WorldObject> { kiln }.Concat(clay).ToList()));

            Assert.IsTrue(potter.Candidates.Any(c => c.Name == Process && c.Duration == WTimeSpan.FromMinutes(120)), "The potter works the kiln on shift even when fed.");
            Assert.IsFalse(farmer.Candidates.Any(c => c.Name == Process), "The farmer of this catalog does not list the task.");
        }

        [TestMethod]
        public void AnOccupation_ProcessesOnShiftOnlyWhatItListed()
        {
            var dir = FindDataDir();
            var catalog = WorkCatalogLoader.Parse(
                File.ReadAllText(Path.Combine(dir, "materials.json")), File.ReadAllText(Path.Combine(dir, "objecttypes.json")), File.ReadAllText(Path.Combine(dir, "tasks.json")),
                """{ "occupations": [ { "id": "miller", "tasks": [ "recipe_flour_from_grain" ] } ] }""");
            var engine = new ContingencySearchEngine(catalog);
            var held = new List<WorldObject> { WorkObjects.CreateItem(catalog, "flour", Loc, new WDateTime(0), null) };
            var oven = WorkObjects.Spawn(catalog, "oven", "oven_1", Loc);

            var miller = engine.Evaluate(ShiftContext("miller", new List<WorldObject> { oven }, held));

            Assert.IsFalse(miller.Candidates.Any(c => c.Name == Process), "A miller does not bake: the oven is not one of their tasks.");
        }

        [TestMethod]
        public void OneWorkstation_ServesSeveralTasks_AndTheSmithDoesWhatTheBarsCover()
        {
            var catalog = WorkCatalogLoader.Parse(
                """{ "materials": [ { "id": "iron", "class": "Metal", "densityKgM3": 7870 } ] }""",
                """
                { "types": [
                  { "id": "forge",  "category": "Ambient", "pickable": false, "weightGrams": 500000, "affordances": { "Production": 1.0 } },
                  { "id": "bar",    "category": "Tool", "pickable": true, "parts": [ { "material": "iron", "volumeCm3": 300 } ] },
                  { "id": "nail",   "category": "Tool", "pickable": true, "parts": [ { "material": "iron", "volumeCm3": 2 } ] },
                  { "id": "spade_head", "category": "Tool", "pickable": true, "parts": [ { "material": "iron", "volumeCm3": 180 } ] } ] }
                """,
                """
                { "tasks": [
                  { "id": "forge_spade_head", "kind": "Craft", "inputs": [ { "item": "bar", "qty": 3 } ], "output": { "item": "spade_head", "qty": 1 }, "durationMinutes": 90, "station": "forge" },
                  { "id": "forge_nail", "kind": "Craft", "inputs": [ { "item": "bar", "qty": 1 } ], "output": { "item": "nail", "qty": 1 }, "durationMinutes": 20, "station": "forge" } ] }
                """,
                """{ "occupations": [ { "id": "smith", "tasks": [ "forge_spade_head", "forge_nail" ] } ] }""");
            var engine = new ContingencySearchEngine(catalog);
            var forge = WorkObjects.Spawn(catalog, "forge", "forge_1", Loc);
            List<WorldObject> Bars(int n) => Enumerable.Range(0, n).Select(i => WorkObjects.Spawn(catalog, "bar", $"bar_{i}", Loc) with { IsPickable = true }).ToList();

            Assert.IsTrue(catalog.IsStation("forge"));
            CollectionAssert.AreEqual(new[] { "forge_spade_head", "forge_nail" }, catalog.TasksAt("forge").Select(t => t.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "spade_head", "nail" }, new RecipeRegistry(catalog).ForStation("forge").Select(r => r.OutputItemId).ToArray());

            Assert.IsTrue(engine.Evaluate(ShiftContext("smith", new List<WorldObject> { forge }.Concat(Bars(3)).ToList()))
                .Candidates.Any(c => c.Name == Process && c.Duration == WTimeSpan.FromMinutes(90)), "Three bars: a spade head.");
            Assert.IsTrue(engine.Evaluate(ShiftContext("smith", new List<WorldObject> { forge }.Concat(Bars(1)).ToList()))
                .Candidates.Any(c => c.Name == Process && c.Duration == WTimeSpan.FromMinutes(20)), "One bar: only a nail, from the same forge.");
            Assert.IsFalse(engine.Evaluate(ShiftContext("smith", new List<WorldObject> { forge })).Candidates.Any(c => c.Name == Process), "No bar, nothing to forge.");
        }

        [TestMethod]
        public void ATaskNeedsAStationThatIsAnObjectTypeOfTheCatalog()
        {
            var e = Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, Types,
                """{ "tasks": [ { "id": "make_pot", "kind": "Craft", "inputs": [ { "item": "clay_lump", "qty": 2 } ], "output": { "item": "pot", "qty": 1 }, "durationMinutes": 120, "station": "nowhere" } ] }"""));
            StringAssert.Contains(e.Message, "nowhere");
            Assert.ThrowsExactly<FormatException>(() => WorkCatalogLoader.Parse(Materials, Types,
                """{ "tasks": [ { "id": "make_pot", "kind": "Craft", "inputs": [ { "item": "clay_lump", "qty": 2 } ], "output": { "item": "pot", "qty": 1 }, "durationMinutes": 120 } ] }"""));
        }

        private static string FindDataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GameEngineTools", "World", "Data", "Work")))
                dir = dir.Parent;
            if (dir is null)
                Assert.Inconclusive("The repository folders were not found next to the test binaries.");
            return Path.Combine(dir!.FullName, "GameEngineTools", "World", "Data", "Work");
        }

        private static BehaviorContext ShiftContext(string occupation, List<WorldObject> available, List<WorldObject>? held = null)
        {
            var ctx = BehaviorComponentTestFactory.Context(hunger: 10, thirst: 5, now: new WDateTime(0));
            var slot = new ScheduleSlot("work", 5, Work);
            ((HumanContext)ctx.HumanContext).Snapshot = ctx.HumanContext.Snapshot with
            {
                Schedule = new DailyScheduleState(new[] { slot }, slot, 0, occupation),
            };
            return ctx with { AvailableObjects = available, HeldObjects = held ?? new List<WorldObject>() };
        }
    }
}
