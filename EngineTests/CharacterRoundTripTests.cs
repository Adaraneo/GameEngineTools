// CharacterRoundTripTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using EngineTests.Utils;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Exports a lived-in character and imports it again: nothing in its engine snapshot may be lost or altered.</summary>
    [TestClass]
    public sealed class CharacterRoundTripTests : TestBase
    {
        [TestMethod]
        public void ExportThenImport_PreservesTheWholeEnginesSnapshot()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            var dt = WTimeSpan.FromHours(1);
            for (var i = 0; i < 24 * 20; i++)
            {
                person.Tick(now, dt);
                now += dt;
            }

            AssertRoundTrips(person);
        }

        [TestMethod]
        public void ExportThenImport_InTheMiddleOfASleep_KeepsTheRunningSession()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: GameEngineTools.Characters.Core.SexBiology.Female, minAge: 25);
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            var dt = WTimeSpan.FromMinutes(30);
            for (var i = 0; i < 48 * 4 && person.Snapshot.Behavior.ActiveSleep is null; i++)
            {
                person.Tick(now, dt);
                if (person.LastOutbox.OfType<GameEngineTools.Characters.Engines.Sleep.SleepPromptRequested>().Any())
                    person.ReceiveEvent(new GameEngineTools.Characters.Engines.Sleep.SleepConfirmed(now, person.Id, default));
                now += dt;
            }

            Assert.IsNotNull(person.Snapshot.Behavior.ActiveSleep, "The scenario must reach a running sleep.");
            AssertRoundTrips(person);
        }

        [TestMethod]
        public void ExportThenImport_WithRelationshipsAndMemories_KeepsThem()
        {
            var a = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);
            var b = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            var dt = WTimeSpan.FromHours(1);

            a.ReceiveEvent(new GameEngineTools.Characters.Engines.Relationships.FirstImpressionFormed(now, a.Id, b.Id, Like: 60, Attraction: 50));
            for (var day = 0; day < 10; day++)
            {
                a.ReceiveEvent(new GameEngineTools.Characters.Engines.Interactions.InteractionOutcome(
                    now, a.Id, b.Id, Accepted: day % 4 != 3, "talk", GameEngineTools.Dialogue.Contracts.RelationalActKind.SmallTalk));
                for (var h = 0; h < 24; h++)
                {
                    a.Tick(now, dt);
                    now += dt;
                }
            }

            Assert.IsTrue(a.Snapshot.Relationships.Edges.ContainsKey(b.Id), "The scenario must produce a relationship edge.");
            Assert.IsTrue(a.Snapshot.Memory.Episodes.Count > 0, "The scenario must produce memories.");
            AssertRoundTrips(a);
        }

        [TestMethod]
        public void ExportedFile_CarriesAFormatVersionAndWritesEnumsAsNames_WithoutLeavingATempFile()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: GameEngineTools.Characters.Core.SexBiology.Female, minAge: 25);

            var file = GeneratedFile.Export(new NPC(100, person));
            var path = Path.Combine(GeneratedFile.NPCDirectory, file);
            var json = File.ReadAllText(path);

            StringAssert.Contains(json, "\"SchemaVersion\": 1");
            StringAssert.Contains(json, "\"Biology\": \"Female\"");
            Assert.IsFalse(File.Exists(path + ".tmp"), "The temp file is renamed away.");
        }

        [TestMethod]
        public void LegacyFile_WithoutVersionAndWithNumericEnums_StillImports()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: GameEngineTools.Characters.Core.SexBiology.Female, minAge: 25);
            var file = GeneratedFile.Export(new NPC(100, person));
            var path = Path.Combine(GeneratedFile.NPCDirectory, file);
            var legacy = File.ReadAllText(path)
                .Replace("\"SchemaVersion\": 1,", string.Empty)
                .Replace("\"Biology\": \"Female\"", $"\"Biology\": {(int)GameEngineTools.Characters.Core.SexBiology.Female}");
            File.WriteAllText(path, legacy);

            var imported = GeneratedFile.ImportNPC(file).Person;

            Assert.AreEqual(person.Id, imported.Id);
            Assert.AreEqual(GameEngineTools.Characters.Core.SexBiology.Female, imported.Biology);
        }

        [TestMethod]
        public void FileFromANewerBuild_IsRefused()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);
            var file = GeneratedFile.Export(new NPC(100, person));
            var path = Path.Combine(GeneratedFile.NPCDirectory, file);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"));

            Assert.Throws<InvalidDataException>(() => GeneratedFile.ImportNPC(file));
        }

        private void AssertRoundTrips(GameEngineTools.Characters.Core.IHuman person)
        {
            var file = GeneratedFile.Export(new NPC(100, person));
            var imported = GeneratedFile.ImportNPC(file).Person;

            var diffs = new List<string>();
            DeepCompare.Compare("Snapshot", person.Snapshot, imported.Snapshot, diffs);

            Console.WriteLine($"{diffs.Count} difference(s):");
            foreach (var d in diffs.Take(60))
                Console.WriteLine("  " + d);

            Assert.AreEqual(0, diffs.Count, "The imported snapshot differs from the exported one (see the list above).");
        }


    }
}
