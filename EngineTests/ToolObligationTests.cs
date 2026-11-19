// ToolObligationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>A borrower remembers that a tool is owed back: the loan makes an episode, a reminder renews it, and only the borrower has it.</summary>
    [TestClass]
    public sealed class ToolObligationTests : TestBase
    {
        private static readonly HumanId Borrower = new(Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"));
        private static readonly HumanId Owner = new(Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b"));

        private static DefaultMemoryEngine BuildEngine()
        {
            var sp = new ServiceCollection()
                .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
                .AddSingleton(Options.Create(new MemoryConfig()))
                .BuildServiceProvider();
            return new DefaultMemoryEngine(sp.GetRequiredService<IOptions<MemoryConfig>>(), sp.GetRequiredService<ILoggerFactory>());
        }

        private static IHumanContext BuildContext(HumanId self, double conscientiousness)
        {
            var personality = new Personality(
                new BigFive(0.5, conscientiousness, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4), Sociosexuality.Intermediate, Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, 0.2, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = self,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler(),
            };
        }

        [TestMethod]
        public void ALoan_MakesTheBorrowerAnEpisode_ButNotTheOwner()
        {
            var borrowerMemory = BuildEngine();
            var ownerMemory = BuildEngine();
            var loan = new ToolLent(new WDateTime(0), Owner, Borrower, "spade_1");

            borrowerMemory.Handle(loan, BuildContext(Borrower, 0.5), new EventCollector());
            ownerMemory.Handle(loan, BuildContext(Owner, 0.5), new EventCollector());

            var episode = borrowerMemory.State.Episodes.Single();
            Assert.AreEqual(ToolObligation.What("spade_1"), episode.What);
            Assert.AreEqual(Owner, episode.OtherPerson);
            Assert.AreEqual(EmotionalTag.Negative, episode.Emotion);
            Assert.AreEqual(0, ownerMemory.State.Episodes.Count(e => e.What == ToolObligation.What("spade_1")), "The owner is owed a tool, not the one who owes it.");
        }

        [TestMethod]
        public void AReminder_RenewsTheSameEpisode_InsteadOfAddingAnother()
        {
            var engine = BuildEngine();
            var ctx = BuildContext(Borrower, 0.5);
            engine.Handle(new ToolLent(new WDateTime(0), Owner, Borrower, "spade_1"), ctx, new EventCollector());
            engine.Tick(new WDateTime(0) + WTimeSpan.FromDays(10), WTimeSpan.FromDays(10), ctx, new EventCollector());
            var faded = engine.State.Episodes.Single().Strength;

            engine.Handle(new ToolOverdue(new WDateTime(0) + WTimeSpan.FromDays(10), Owner, Borrower, "spade_1"), ctx, new EventCollector());

            Assert.AreEqual(1, engine.State.Episodes.Count);
            Assert.IsTrue(engine.State.Episodes.Single().Strength > faded, "A reminder renews the memory.");
        }

        [TestMethod]
        public void TheMemory_FadesWithoutAReminder_AndTwoToolsAreTwoEpisodes()
        {
            var engine = BuildEngine();
            var ctx = BuildContext(Borrower, 0.5);
            engine.Handle(new ToolLent(new WDateTime(0), Owner, Borrower, "spade_1"), ctx, new EventCollector());
            engine.Handle(new ToolLent(new WDateTime(0), Owner, Borrower, "sickle_1"), ctx, new EventCollector());
            var before = engine.State.Episodes.Single(e => e.What == ToolObligation.What("spade_1")).Strength;

            engine.Tick(new WDateTime(0) + WTimeSpan.FromDays(20), WTimeSpan.FromDays(20), ctx, new EventCollector());

            Assert.AreEqual(2, engine.State.Episodes.Count(e => e.What.StartsWith("Obligation:", StringComparison.Ordinal)));
            Assert.IsTrue(engine.State.Episodes.Single(e => e.What == ToolObligation.What("spade_1")).Strength < before);
        }

        [TestMethod]
        public void AConscientiousBorrower_WhoThinksWellOfTheOwner_FeelsTheObligationMore()
        {
            Assert.IsTrue(ToolObligation.Salience(0.9, 0.8) > ToolObligation.Salience(0.2, 0.0));
            Assert.IsTrue(ToolObligation.Salience(0.0, 0.0) > 0.0 && ToolObligation.Salience(1.0, 1.0) <= 1.0);
        }

        [TestMethod]
        public void TheWalkUtility_RisesWithStrength_AndNoMemoryAddsNothing()
        {
            Assert.AreEqual(56.0, ToolObligation.WalkUtility(56.0, null));
            Assert.IsTrue(ToolObligation.WalkUtility(56.0, 0.9) > ToolObligation.WalkUtility(56.0, 0.3));
            Assert.IsFalse(ToolObligation.FeltOnShift(null));
            Assert.IsFalse(ToolObligation.FeltOnShift(0.5));
            Assert.IsTrue(ToolObligation.FeltOnShift(0.8));
        }

        [TestMethod]
        public void TheToolIsBroughtBackNow_OffShiftAlways_AndOnShiftOnlyWhenStronglyFelt()
        {
            Assert.IsTrue(ToolObligation.ReturnsNow(false, null));
            Assert.IsFalse(ToolObligation.ReturnsNow(true, null));
            Assert.IsFalse(ToolObligation.ReturnsNow(true, 0.4));
            Assert.IsTrue(ToolObligation.ReturnsNow(true, 0.8));
        }
    }
}
