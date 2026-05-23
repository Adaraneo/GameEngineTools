// ContingencySearchEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class ContingencySearchEngineTests : TestBase
    {
        private ContingencySearchEngine _engine = null!;

        [TestInitialize]
        public void Setup()
        {
            _engine = new ContingencySearchEngine();
        }

        // ── No provider wired ──────────────────────────────────────────────────────

        /// <summary>
        /// When AvailableObjects is null (no provider), engine must be a no-op.
        /// </summary>
        [TestMethod]
        public void Evaluate_NoProvider_ReturnsEmpty()
        {
            var context = BehaviorComponentTestFactory.Context(hunger: 90, thirst: 80);
            // Default context has AvailableObjects = null

            var output = _engine.Evaluate(context);

            Assert.AreEqual(0, output.Candidates.Count,
                "ContingencySearchEngine must be a no-op when no provider is wired.");
        }

        // ── Food foraging ──────────────────────────────────────────────────────────

        /// <summary>
        /// Hungry character in a location with no food objects gets a MoveTo:Food candidate.
        /// </summary>
        [TestMethod]
        public void Evaluate_HungryNoFood_GeneratesMoveToFoodCandidate()
        {
            var context = ContextWithObjects(hunger: 80, thirst: 10, objects: new List<WorldObject>());

            var output = _engine.Evaluate(context);

            Assert.IsTrue(output.Candidates.Any(c => c.Name == MoveToFood),
                "Hungry character with no food nearby must receive a MoveTo:Food candidate.");
        }

        /// <summary>
        /// When food is present, no MoveTo:Food candidate should be generated.
        /// The primary Eat candidate will survive gating instead.
        /// </summary>
        [TestMethod]
        public void Evaluate_HungryFoodPresent_NoMoveToFoodCandidate()
        {
            var objects = new List<WorldObject> { MakeObject("bread", WorldObjectCategory.Food) };
            var context = ContextWithObjects(hunger: 80, thirst: 10, objects);

            var output = _engine.Evaluate(context);

            Assert.IsFalse(output.Candidates.Any(c => c.Name == MoveToFood),
                "MoveTo:Food must NOT be generated when food is already at current location.");
        }

        /// <summary>
        /// Need below threshold must not produce a foraging candidate —
        /// the character is not hungry enough to bother searching.
        /// </summary>
        [TestMethod]
        public void Evaluate_NeedBelowThreshold_NoCandidate()
        {
            // Need = 10, threshold = 20
            var context = ContextWithObjects(hunger: 10, thirst: 5, objects: new List<WorldObject>());

            var output = _engine.Evaluate(context);

            Assert.IsFalse(output.Candidates.Any(c => c.Name == MoveToFood),
                "Need below MinNeedToSearch must not trigger foraging.");
            Assert.IsFalse(output.Candidates.Any(c => c.Name == MoveToDrink),
                "Need below MinNeedToSearch must not trigger foraging.");
        }

        // ── Drink foraging ─────────────────────────────────────────────────────────

        /// <summary>
        /// Thirsty character in a location with no drink objects gets a MoveTo:Drink candidate.
        /// </summary>
        [TestMethod]
        public void Evaluate_ThirstyNoDrink_GeneratesMoveToDrinkCandidate()
        {
            var context = ContextWithObjects(hunger: 10, thirst: 75, objects: new List<WorldObject>());

            var output = _engine.Evaluate(context);

            Assert.IsTrue(output.Candidates.Any(c => c.Name == MoveToDrink),
                "Thirsty character with no drink nearby must receive a MoveTo:Drink candidate.");
        }

        // ── Utility calibration ────────────────────────────────────────────────────

        /// <summary>
        /// MoveTo:Food utility must be lower than what Eat would have had at the same need,
        /// so that actual eating always wins over foraging when both are possible.
        /// </summary>
        [TestMethod]
        public void Evaluate_MoveToFoodUtility_BelowEatUtility()
        {
            var context = ContextWithObjects(hunger: 80, thirst: 0, objects: new List<WorldObject>());

            var output = _engine.Evaluate(context);

            var foragingUtility = output.Candidates.First(c => c.Name == MoveToFood).Utility;
            var eatUtility = BehaviorMath.Util(80, 1.2); // Eat weight from PhysiologicalNeedsEngine

            Assert.IsTrue(foragingUtility < eatUtility,
                $"MoveTo:Food utility ({foragingUtility:F1}) must be below Eat utility ({eatUtility:F1}).");
        }

        // ── Both needs simultaneously ──────────────────────────────────────────────

        /// <summary>
        /// When both food and drink are absent and both needs are above threshold,
        /// both foraging candidates are generated simultaneously.
        /// </summary>
        [TestMethod]
        public void Evaluate_BothNeedsHighNeitherPresent_GeneratesBothCandidates()
        {
            var context = ContextWithObjects(hunger: 70, thirst: 65, objects: new List<WorldObject>());

            var output = _engine.Evaluate(context);

            Assert.IsTrue(output.Candidates.Any(c => c.Name == MoveToFood));
            Assert.IsTrue(output.Candidates.Any(c => c.Name == MoveToDrink));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static BehaviorContext ContextWithObjects(
            double hunger,
            double thirst,
            IReadOnlyList<WorldObject> objects)
        {
            // Use BehaviorComponentTestFactory but override AvailableObjects via 'with'
            var ctx = BehaviorComponentTestFactory.Context(hunger: hunger, thirst: thirst);
            return ctx with { AvailableObjects = objects };
        }

        private static WorldObject MakeObject(string id, WorldObjectCategory category)
            => new WorldObject
            {
                Id = id,
                DisplayName = id,
                Category = category,
                LocationId = "test",
                IsAvailable = true,
                Affordances = ImmutableArray<WorldObjectAffordance>.Empty
            };
    }
}