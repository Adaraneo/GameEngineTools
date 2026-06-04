// TheoryOfMindTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.ToM;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for R1 Theory of Mind L2: mutual-knowledge formation, per-NPC ToM ceiling
    /// distribution, and stress-driven depth degradation.
    /// </summary>
    [TestClass]
    public class TheoryOfMindTests : TestBase
    {
        #region Test 1 — mutual knowledge set when both witness the same event

        [TestMethod]
        public void ToM_MutualKnowledge_SetWhenBothWitness()
        {
            var a = new HumanId(Guid.NewGuid());
            var b = new HumanId(Guid.NewGuid());

            var engineA = ServiceProvider.GetRequiredService<IMemoryEngine>();
            var engineB = ServiceProvider.GetRequiredService<IMemoryEngine>();
            engineA.RestoreState(new MemoryIndex(new List<EpisodicMemory>()));
            engineB.RestoreState(new MemoryIndex(new List<EpisodicMemory>()));

            // An accepted self-disclosure A→B: both parties are present, so it is common ground.
            var ev = new InteractionOutcome(At(100), a, b, Accepted: true, "ok", SpeechAct.SelfDisclosure);

            engineA.Handle(ev, BuildCtx(a), new EventCollector());
            engineB.Handle(ev, BuildCtx(b), new EventCollector());

            var aFact = engineA.State.Knowledge.FirstOrDefault(f => f.IsMutuallyKnown);
            var bFact = engineB.State.Knowledge.FirstOrDefault(f => f.IsMutuallyKnown);

            Assert.IsNotNull(aFact, "A must hold a mutually-known fact about the shared event.");
            Assert.IsNotNull(bFact, "B must hold a mutually-known fact about the shared event.");
            Assert.AreEqual(b, aFact!.KnownSharedWith, "A's fact must be shared with B.");
            Assert.AreEqual(a, bFact!.KnownSharedWith, "B's fact must be shared with A.");
        }

        #endregion

        #region Test 2 — MutualKnowledgeFormed event emitted

        [TestMethod]
        public void ToM_MutualKnowledge_EmitsEvent()
        {
            var a = new HumanId(Guid.NewGuid());
            var b = new HumanId(Guid.NewGuid());

            var engineB = ServiceProvider.GetRequiredService<IMemoryEngine>();
            engineB.RestoreState(new MemoryIndex(new List<EpisodicMemory>()));

            var outbox = new EventCollector();
            engineB.Handle(new InteractionOutcome(At(100), a, b, true, "ok", SpeechAct.SelfDisclosure),
                BuildCtx(b), outbox);

            var formed = outbox.Drain().OfType<MutualKnowledgeFormed>().FirstOrDefault();
            Assert.IsNotNull(formed, "MutualKnowledgeFormed must be emitted on common-ground formation.");
            Assert.AreEqual(a, formed!.SharedWith);
        }

        #endregion

        #region Test 3 — ceiling distribution: mean ≈ 4, SD ≈ 1

        [TestMethod]
        public void ToMCeiling_Distribution_MeanFourSdOne()
        {
            var rng = new SeededRandom(12345);
            const int n = 5000;
            var samples = new int[n];
            for (var i = 0; i < n; i++)
                samples[i] = ToMMath.GenerateCeiling(rng);

            var mean = samples.Average();
            var variance = samples.Select(s => (s - mean) * (s - mean)).Average();
            var sd = Math.Sqrt(variance);

            Assert.IsTrue(Math.Abs(mean - ToMMath.CeilingMean) < 0.2,
                $"Ceiling mean must be ≈ {ToMMath.CeilingMean}. Got: {mean:F3}");
            Assert.IsTrue(Math.Abs(sd - ToMMath.CeilingSd) < 0.25,
                $"Ceiling SD must be ≈ {ToMMath.CeilingSd}. Got: {sd:F3}");
            Assert.IsTrue(samples.All(s => s is >= 1 and <= 8), "Ceilings must be clamped to [1, 8].");
        }

        #endregion

        #region Test 4 — depth degrades under stress

        [TestMethod]
        public void ToM_UnderStress_DepthDegrades()
        {
            const int ceiling = 4;

            Assert.AreEqual(4, ToMMath.EffectiveToMDepth(ceiling, stress: 10),
                "Low stress: full depth.");
            Assert.AreEqual(3, ToMMath.EffectiveToMDepth(ceiling, stress: 50),
                "Moderate stress: −1 level.");
            Assert.AreEqual(2, ToMMath.EffectiveToMDepth(ceiling, stress: 85),
                "High stress: −2 levels.");

            // Degradation is 1–2 levels and never collapses below 1.
            Assert.IsTrue(ToMMath.EffectiveToMDepth(1, stress: 95) >= 1,
                "Effective depth must never drop below 1.");
        }

        #endregion

        #region Test 5 — per-NPC ceiling is carried on Personality (sampled by generator)

        [TestMethod]
        public void ToMCeiling_GeneratedPersonality_PopulationCeilingInRange()
        {
            var gen = ServiceProvider.GetService<GameEngineTools.Characters.Generation.IPersonalityGenerator>();
            if (gen is null)
            {
                Assert.Inconclusive("IPersonalityGenerator not registered in this test host.");
                return;
            }

            const int n = 300;
            var ceilings = new List<int>(n);
            for (var i = 0; i < n; i++)
                ceilings.Add(gen.Generate(seed: 1000 + i).ToMCeiling);

            var mean = ceilings.Average();
            Assert.IsTrue(mean is > 3.4 and < 4.6,
                $"Generated population ToM ceiling mean must be ≈ 4. Got: {mean:F2}");
            Assert.IsTrue(ceilings.All(c => c is >= 1 and <= 8));
        }

        #endregion

        #region Helpers

        private static WDateTime At(int year) => WDateOnly.New(year, 1, 1).ToDateTime();

        private static IHumanContext BuildCtx(HumanId self)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate, Chronotype.Neutral);

            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.0, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "T", Familiar = new[] { "T" } },
                    new Surname { Male = "H", Female = "H" },
                    WDateOnly.New(80, 1, 1)),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _r;
            public SeededRandom(int seed) => _r = new Random(seed);
            public int Next(int min, int max) => _r.Next(min, max);
            public double NextUnit() => _r.NextDouble();
            public bool Chance(double p) => _r.NextDouble() < p;
        }

        #endregion
    }
}
