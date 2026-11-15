// CharacterResetTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using EngineTests.Utils;
    using GameEngineTools;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Simulation;
    using Microsoft.Extensions.DependencyInjection;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>A character returns to the state it had after generation: engines, inbox, scheduled events and a running sleep.</summary>
    [TestClass]
    public sealed class CharacterResetTests : TestBase
    {
        private static readonly WDateTime Start = WDateTime.New(WDateOnly.New(100, 1, 1));

        private static WDateTime Live(GameEngineTools.Characters.Core.IHuman person, WDateTime from, int hours, GameEngineTools.Characters.Core.IHuman? other = null)
        {
            var now = from;
            var dt = WTimeSpan.FromHours(1);
            for (var i = 0; i < hours; i++)
            {
                if (other is not null && i % 24 == 0)
                {
                    person.ReceiveEvent(new FirstImpressionFormed(now, person.Id, other.Id, Like: 60, Attraction: 50));
                    person.ReceiveEvent(new InteractionOutcome(now, person.Id, other.Id, Accepted: true, "talk", GameEngineTools.Dialogue.Contracts.RelationalActKind.SmallTalk));
                }

                person.Tick(now, dt);
                now += dt;
            }

            return now;
        }

        [TestMethod]
        public void AfterTwentyDaysOfLife_ResetReturnsTheSnapshotToWhatItWasAfterGeneration()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);
            var other = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);
            var initial = person.Snapshot;

            Live(person, Start, 24 * 20, other);

            var diffsBefore = new List<string>();
            DeepCompare.Compare("Snapshot", initial, person.Snapshot, diffsBefore);
            Assert.IsTrue(diffsBefore.Count > 0, "Twenty days of life must change something, or the test proves nothing.");
            Assert.IsTrue(person.Snapshot.Relationships.Edges.ContainsKey(other.Id), "The scenario must produce a relationship.");

            person.ResetToInitialState(Start);

            var diffs = new List<string>();
            DeepCompare.Compare("Snapshot", initial, person.Snapshot, diffs);
            Console.WriteLine($"{diffs.Count} difference(s):");
            foreach (var d in diffs.Take(30)) Console.WriteLine("  " + d);
            Assert.AreEqual(0, diffs.Count, "After a reset the snapshot must equal the one from generation.");
        }

        [TestMethod]
        public void ResetInTheMiddleOfASleep_EndsTheSleepAndDropsPendingEvents()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: GameEngineTools.Characters.Core.SexBiology.Female, minAge: 25);
            var now = Start;
            var dt = WTimeSpan.FromMinutes(30);
            for (var i = 0; i < 48 * 4 && person.Snapshot.Behavior.ActiveSleep is null; i++)
            {
                person.Tick(now, dt);
                if (person.LastOutbox.OfType<GameEngineTools.Characters.Engines.Sleep.SleepPromptRequested>().Any())
                    person.ReceiveEvent(new GameEngineTools.Characters.Engines.Sleep.SleepConfirmed(now, person.Id, default));
                now += dt;
            }

            Assert.IsNotNull(person.Snapshot.Behavior.ActiveSleep, "The scenario must reach a running sleep.");
            person.ReceiveEvent(new FirstImpressionFormed(now, person.Id, new GameEngineTools.Characters.Core.HumanId(Guid.NewGuid()), Like: 50, Attraction: 50));

            person.ResetToInitialState(Start);

            Assert.IsNull(person.Snapshot.Behavior.ActiveSleep, "A reset ends the sleep.");
            person.Tick(Start, WTimeSpan.FromMinutes(30));
            Assert.IsFalse(person.Snapshot.Relationships.Edges.Count > 0, "The queued event of the old run must not arrive after the reset.");
        }

        [TestMethod]
        public void RecapturedState_IsWhatTheCharacterReturnsTo()
        {
            var person = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);
            var other = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 25);

            person.ReceiveEvent(new FirstImpressionFormed(Start, person.Id, other.Id, Like: 70, Attraction: 50));
            person.FlushInbox();
            person.CaptureInitialState();
            var captured = person.Snapshot;

            Live(person, Start, 24 * 5);
            person.ResetToInitialState(Start);

            var diffs = new List<string>();
            DeepCompare.Compare("Snapshot", captured, person.Snapshot, diffs);
            Assert.AreEqual(0, diffs.Count, string.Join("; ", diffs.Take(5)));
            Assert.IsTrue(person.Snapshot.Relationships.Edges.ContainsKey(other.Id), "What the host wired after generation stays.");
        }
        [TestMethod]
        public void SceneReset_ReturnsEveryCharacterToItsGeneratedState_AndTellsTheHost()
        {
            var clock = new SystemClock(ServiceProvider.GetRequiredService<IWorldClock>(), ServiceProvider.GetRequiredService<WorldTimeSpec>());
            clock.SetNow(Start);
            var person = CharacterManager.RandomizePerson(maxAge: 35, sexBiology: null, minAge: 18);
            var initial = person.Snapshot;
            SimulationScene? scene = null;
            var ticks = 0;
            EnginesSnapshot? atReset = null;
            var told = 0;
            var options = new SimulationSceneOptions
            {
                Characters = new List<IHuman> { person },
                SimulationDays = 2,
                TickStep = WTimeSpan.FromHours(1),
                OnTick = (_, _) => { if (++ticks == 30) scene!.ResetCharactersToInitialState(); },
                OnCharactersReset = (_, chars) => { told++; atReset = chars[0].Snapshot; },
            };
            scene = new SimulationScene(clock, options, ServiceProvider.GetRequiredService<ICognitiveResolutionLevelRuntime>());

            scene.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, told, "The host is told once.");
            var diffs = new List<string>();
            DeepCompare.Compare("Snapshot", initial, atReset!, diffs);
            Assert.AreEqual(0, diffs.Count, string.Join("; ", diffs.Take(5)));
        }
    }
}
