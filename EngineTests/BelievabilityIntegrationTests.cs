// BelievabilityIntegrationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using System;

    /// <summary>
    /// Cross-cutting integration test (research plan §Cross-cutting #1): run the believability
    /// subsystems together through the real orchestrator over a long horizon and verify that no
    /// drifting state runs away to an extreme — the per-state regression brakes hold under load.
    /// </summary>
    [TestClass]
    public class BelievabilityIntegrationTests : TestBase
    {
        [TestMethod]
        public void Believability_MultipleSystems_NoStateRunsAway()
        {
            var factory = ServiceProvider.GetRequiredService<IHumanFactory>();
            var rngFactory = ServiceProvider.GetRequiredService<IRandomSourceFactory>();
            var clock = ServiceProvider.GetRequiredService<IClock>();

            var nowYear = clock.Now.Date.Year;
            var human = factory.Create(BuildAdultBlueprint(rngFactory, birthYear: nowYear - 35));

            // Sanity: the new believability states are present from creation.
            Assert.IsNotNull(human.Snapshot.Values, "Values state must be seeded at creation.");
            Assert.IsNotNull(human.Snapshot.SelfConcept, "Self-concept must be seeded at creation.");
            Assert.IsNotNull(human.Snapshot.Interests, "Interests must be seeded at creation.");

            var baselineBenevolence = human.Snapshot.Values!.Baseline.Benevolence;

            var now = clock.Now;
            var self = human.Id;

            // Drive value-violating + affirming pressure for ~6 game years while ticking monthly.
            for (var i = 0; i < 72; i++)
            {
                human.ReceiveEvent(new ValueCongruenceViolated(
                    now, self, ActionNames.Work, -0.5, nameof(ValuesProfile.Benevolence)));
                human.ReceiveEvent(new ActionCommitted(now, self, ActionNames.Create, WTimeSpan.FromHours(1)));

                now += WTimeSpan.FromDays(30);
                human.Tick(now, WTimeSpan.FromDays(30));
            }

            // Every drifting state must remain finite and within bounds — no runaway.
            AssertProfileBounded(human.Snapshot.Values!.Current);
            AssertProfileBounded(human.Snapshot.Values!.Baseline);
            AssertInterestsBounded(human.Snapshot.Interests!.Current);

            var sc = human.Snapshot.SelfConcept!;
            AssertUnit(sc.PerceivedOpenness); AssertUnit(sc.PerceivedConscientiousness);
            AssertUnit(sc.PerceivedExtraversion); AssertUnit(sc.PerceivedAgreeableness);
            AssertUnit(sc.PerceivedNeuroticism); AssertUnit(sc.SelfEsteem);
            AssertUnit(sc.SelfDiscrepancy);

            // Emergent: sustained anti-Benevolence pressure eroded the held value below its baseline,
            // but the value stayed clamped (never ran away below 0).
            Assert.IsTrue(human.Snapshot.Values!.Current.Benevolence < baselineBenevolence,
                "Sustained violations must erode the held Benevolence value.");
            Assert.IsTrue(human.Snapshot.Values!.Current.Benevolence >= 0.0,
                "Eroded value must stay clamped at/above 0 — no runaway.");
        }

        #region Helpers

        private static HumanBlueprint BuildAdultBlueprint(IRandomSourceFactory rngFactory, int birthYear)
        {
            var id = new HumanId(Guid.NewGuid());
            var identity = new Identity(
                new Name { Original = "Iva", Familiar = new[] { "Iva" } },
                new Surname { Male = "Test", Female = "Test" },
                WDateOnly.New(birthYear, 1, 1));

            var personality = new Personality(
                new BigFive(0.6, 0.5, 0.5, 0.55, 0.4),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var geneticBlueprint = new AppearanceGenerator(rngFactory).GenerateBlueprint(SexBiology.Female, seed: 7);

            return new HumanBlueprint(id, identity, SexBiology.Female, personality, geneticBlueprint, Seed: 7);
        }

        private static void AssertProfileBounded(ValuesProfile p)
        {
            AssertUnit(p.Benevolence); AssertUnit(p.Universalism); AssertUnit(p.SelfDirection);
            AssertUnit(p.Stimulation); AssertUnit(p.Hedonism); AssertUnit(p.Achievement);
            AssertUnit(p.Power); AssertUnit(p.Security); AssertUnit(p.Conformity); AssertUnit(p.Tradition);
        }

        private static void AssertInterestsBounded(InterestProfile p)
        {
            AssertUnit(p.Realistic); AssertUnit(p.Investigative); AssertUnit(p.Artistic);
            AssertUnit(p.Social); AssertUnit(p.Enterprising); AssertUnit(p.Conventional);
        }

        private static void AssertUnit(double v)
        {
            Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v), "State must stay finite.");
            Assert.IsTrue(v is >= 0.0 and <= 1.0, $"State must stay within [0,1]. Got: {v}");
        }

        #endregion
    }
}
