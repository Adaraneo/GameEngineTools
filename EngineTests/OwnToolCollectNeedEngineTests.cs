// OwnToolCollectNeedEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using EngineTests.Utils;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Work;
    using Grammar.Core.Enums;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>A character who sees his own tool lying away from its shed may take it back: a soft candidate, not a rule.</summary>
    [TestClass]
    public sealed class OwnToolCollectNeedEngineTests : TestBase
    {
        private const string Loc = "test"; // BehaviorComponentTestFactory surface location

        private static readonly HumanId Self = new(Guid.NewGuid());

        private static WorldObject Spade(string id, string? shed, HumanId? owner)
        {
            return WorkObjects.Spawn(TestWork.Catalog, "spade", id, Loc, owner) with { StoredAt = shed };
        }

        private static BehaviorContext Ctx(double conscientiousness, IReadOnlyList<WorldObject> lying, params WorldObject[] held)
        {
            var personality = new Personality(
                new BigFive(0.5, conscientiousness, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4), Sociosexuality.Intermediate, Chronotype.Neutral);
            var template = BehaviorComponentTestFactory.Context(selfId: Self, personality: personality);
            return template with { AvailableObjects = lying, HeldObjects = held.ToList() };
        }

        [TestMethod]
        public void AnOwnToolLyingAwayFromItsShed_IsOfferedToBeTaken_MoreReadilyBySomeoneConscientious()
        {
            var engine = new OwnToolCollectNeedEngine();
            var lying = new[] { Spade("spade_1", "farm", Self) };

            var careless = engine.Evaluate(Ctx(0.1, lying)).Candidates.Single();
            var careful = engine.Evaluate(Ctx(0.9, lying)).Candidates.Single();

            Assert.AreEqual(InteractWithObject, careless.Name);
            Assert.AreEqual(ObjectInteractionKind.Take, careless.ObjectInteraction!.Kind);
            Assert.IsTrue(careful.Utility > careless.Utility, "A soft pull: the careful take it back more readily.");
            Assert.IsTrue(careful.Utility < 90.0, "Never above a hard need such as a put-down at the shed.");
        }

        [TestMethod]
        public void NothingIsOffered_ForAToolInItsShed_ForSomeoneElsesTool_ForALentOne_OrWhenBothHandsAreFull()
        {
            var engine = new OwnToolCollectNeedEngine();
            var other = new HumanId(Guid.NewGuid());

            Assert.AreEqual(0, engine.Evaluate(Ctx(0.5, new[] { Spade("a", Loc, Self) })).Candidates.Count, "It already lies in its shed.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(0.5, new[] { Spade("b", "farm", other) })).Candidates.Count, "Not his tool.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(0.5, new[] { Spade("c", "farm", Self) with { LentTo = other } })).Candidates.Count, "Lent out.");
            Assert.AreEqual(
                0,
                engine.Evaluate(Ctx(0.5, new[] { Spade("d", "farm", Self) }, Spade("e", "x", Self) with { HeldBy = Self }, Spade("f", "y", Self) with { HeldBy = Self })).Candidates.Count,
                "Two hands, two tools.");
        }
    }
}
