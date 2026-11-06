// SexualEncounterReadinessTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;

    /// <summary>An accepted invitation becomes an encounter with a smooth chance, not behind a wall of thresholds.</summary>
    [TestClass]
    public sealed class SexualEncounterReadinessTests : TestBase
    {
        private static RelationshipEdge Edge(double trust, double comfort, double closeness, double intimate, double sexual) =>
            new(new HumanId(Guid.NewGuid()), new HumanId(Guid.NewGuid()),
                Like: 50, Trust: trust, Familiarity: 50,
                AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: intimate, SexualInterest: sexual,
                Closeness: closeness, Respect: 50, Comfort: comfort,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));

        [TestMethod]
        public void ASeededCouple_HasAUsefulChance()
        {
            var p = DefaultInteractionEngine.SexualEncounterReadinessProbability(Edge(80, 82, 85, 75, 55), stress: 30);
            Assert.IsTrue(p > 0.6, $"{p:F2}");
        }

        [TestMethod]
        public void Acquaintances_AlmostNever()
        {
            var p = DefaultInteractionEngine.SexualEncounterReadinessProbability(Edge(45, 45, 30, 10, 20), stress: 10);
            Assert.IsTrue(p < 0.05, $"{p:F2}");
        }

        [TestMethod]
        public void Stress_LowersTheChance_SmoothlyToZero()
        {
            var edge = Edge(80, 82, 85, 75, 55);
            var calm = DefaultInteractionEngine.SexualEncounterReadinessProbability(edge, 20);
            var tense = DefaultInteractionEngine.SexualEncounterReadinessProbability(edge, 45);
            var frazzled = DefaultInteractionEngine.SexualEncounterReadinessProbability(edge, 70);
            Assert.IsTrue(calm > tense && tense > frazzled && frazzled == 0.0, $"{calm:F2} {tense:F2} {frazzled:F2}");
        }
    }
}
