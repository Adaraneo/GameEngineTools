// CharacterRoundTripTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

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

        private void AssertRoundTrips(GameEngineTools.Characters.Core.IHuman person)
        {
            var file = GeneratedFile.Export(new NPC(100, person));
            var imported = GeneratedFile.ImportNPC(file).Person;

            var diffs = new List<string>();
            Compare("Snapshot", person.Snapshot, imported.Snapshot, diffs, 0);

            Console.WriteLine($"{diffs.Count} difference(s):");
            foreach (var d in diffs.Take(60))
                Console.WriteLine("  " + d);

            Assert.AreEqual(0, diffs.Count, "The imported snapshot differs from the exported one (see the list above).");
        }

        private static void Compare(string path, object? a, object? b, List<string> diffs, int depth)
        {
            if (ReferenceEquals(a, b)) return;
            if (a is null || b is null) { diffs.Add($"{path}: {Show(a)} vs {Show(b)}"); return; }
            if (depth > 14) return;

            var type = a.GetType();
            var collections = a is IEnumerable && b is IEnumerable && a is not string; // an array vs a List is the same data
            if (!collections && type != b.GetType()) { diffs.Add($"{path}: type {type.Name} vs {b.GetType().Name}"); return; }

            if (a is double da)
            {
                var db = (double)b;
                if (!(Math.Abs(da - db) <= 1e-9 * Math.Max(1.0, Math.Abs(da))) && !(double.IsNaN(da) && double.IsNaN(db)))
                    diffs.Add($"{path}: {da:R} vs {db:R}");
                return;
            }

            if (type.IsPrimitive || type.IsEnum || a is string || a is decimal || a is Guid)
            {
                if (!a.Equals(b)) diffs.Add($"{path}: {Show(a)} vs {Show(b)}");
                return;
            }

            if (a is IDictionary dictA && b is IDictionary dictB)
            {
                if (dictA.Count != dictB.Count) { diffs.Add($"{path}: dictionary count {dictA.Count} vs {dictB.Count}"); return; }
                foreach (var key in dictA.Keys)
                {
                    if (!dictB.Contains(key)) { diffs.Add($"{path}[{Show(key)}]: missing after import"); continue; }
                    Compare($"{path}[{Show(key)}]", dictA[key], dictB[key], diffs, depth + 1);
                }

                return;
            }

            if (a is IEnumerable enumA && b is IEnumerable enumB)
            {
                var listA = enumA.Cast<object?>().ToList();
                var listB = enumB.Cast<object?>().ToList();
                if (listA.Count != listB.Count) { diffs.Add($"{path}: count {listA.Count} vs {listB.Count}"); return; }
                for (var i = 0; i < listA.Count; i++)
                    Compare($"{path}[{i}]", listA[i], listB[i], diffs, depth + 1);
                return;
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && !typeof(Delegate).IsAssignableFrom(p.PropertyType))
                .ToList();
            if (props.Count == 0)
            {
                if (!a.Equals(b)) diffs.Add($"{path}: {Show(a)} vs {Show(b)}");
                return;
            }

            foreach (var p in props)
            {
                object? va, vb;
                try { va = p.GetValue(a); vb = p.GetValue(b); }
                catch { continue; }
                Compare($"{path}.{p.Name}", va, vb, diffs, depth + 1);
            }
        }

        private static string Show(object? o) => o is null ? "null" : o.ToString() ?? o.GetType().Name;
    }
}
