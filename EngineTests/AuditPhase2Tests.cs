// AuditPhase2Tests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;

    /// <summary>Regression tests for the Phase 2 fixes from the 2026-10 audit (docs/plans/audit-2026-10-remediation.md).</summary>
    [TestClass]
    public class AuditPhase2Tests : TestBase
    {
        [TestMethod]
        public void MenstrualCycle_WithVariability_KeepsOneLengthPerCycle()
        {
            var cycleCfg = new MenstrualCycleConfig(VariabilityDaysStdDev: 4.0);
            var engine = BuildCycleEngine(cycleCfg, new SeededRandomSource(1234));
            var ctx = BuildContext();

            var prev = engine.State.Cycle!;
            var wraps = 0;
            for (var i = 0; i < 200; i++)
            {
                engine.Tick(new WDateTime(0), WTimeSpan.FromDays(1), ctx, new EventCollector());
                var cur = engine.State.Cycle!;

                Assert.IsTrue(cur.DayInCycle <= cur.CurrentCycleLength,
                    $"Day {cur.DayInCycle} lies beyond the stored cycle length {cur.CurrentCycleLength}.");

                if (cur.DayInCycle == 1)
                {
                    wraps++;
                    Assert.AreEqual(prev.CurrentCycleLength, prev.DayInCycle,
                        "A cycle must wrap exactly on the last day of its stored length.");
                }
                else
                {
                    Assert.AreEqual(prev.DayInCycle + 1, cur.DayInCycle);
                    Assert.AreEqual(prev.CurrentCycleLength, cur.CurrentCycleLength,
                        "The cycle length must not change in the middle of a cycle.");
                }

                prev = cur;
            }

            Assert.IsTrue(wraps >= 5, "Over 200 days the cycle should have wrapped several times.");
        }

        [TestMethod]
        public void ClockConversion_IsAFractionOfTheDay_AndRoundTrips()
        {
            UseDayLength(40);
            Assert.AreEqual(40.0 / 3.0, WWorld.Spec.FromClock24(8), 1e-9);
            Assert.AreEqual(8.0, WWorld.Spec.ToClock24(WWorld.Spec.FromClock24(8)), 1e-9);

            UseDayLength(24);
            Assert.AreEqual(8.0, WWorld.Spec.FromClock24(8), 1e-9);
        }

        [TestMethod]
        public void ChronotypeBonus_FollowsTheClockFractionOnALongDay()
        {
            UseDayLength(40);
            // A lark peaks at 08:00 on a 24-hour clock, i.e. world hour 13.3 of 40 (hour 13 here).
            var atPeak = BehaviorMath.ComputeChronoBonus(WDateTime.New(116, 1, 1, 13), Chronotype.Lark);
            var offPeak = BehaviorMath.ComputeChronoBonus(WDateTime.New(116, 1, 1, 8), Chronotype.Lark);

            Assert.IsTrue(atPeak > 14.0, $"Lark bonus at its peak was {atPeak:F2}.");
            Assert.IsTrue(offPeak < atPeak - 5.0, $"World hour 8 is only 04:48 on the 24-hour clock; bonus {offPeak:F2}.");
        }

        private static IHumanContext BuildContext()
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, 0.2, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
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

        private DefaultPhysiologyEngine BuildCycleEngine(MenstrualCycleConfig cycleCfg, IRandomSource random)
        {
            var cfg = Options.Create(new PhysiologyConfig(
                RestingMetabolicRate: 1600,
                MaxSleepDebtHours: 12,
                EnableMenstrualCycle: true,
                MenstrualCycleBeginsInAge: 12));

            var engine = new DefaultPhysiologyEngine(
                cfg,
                Options.Create(cycleCfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                random,
                biology: SexBiology.Female,
                birthDate: WDateOnly.New(100, 1, 1),
                now: WDateOnly.New(116, 1, 1));

            engine.RestoreState(engine.State with
            {
                Cycle = engine.State.Cycle! with { DayInCycle = 1, AnovulatoryCycleActive = false, StressSuppressionAccDays = 0.0 }
            });
            return engine;
        }

        private sealed class SeededRandomSource : IRandomSource
        {
            private readonly Random _random;

            public SeededRandomSource(int seed) => _random = new Random(seed);

            public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

            public double NextUnit() => _random.NextDouble();

            public bool Chance(double p) => _random.NextDouble() < p;
        }
    }
}
