// ActionStepTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;

    /// <summary>An action shorter than the simulation step must have the same effect whatever the step: the plan lasts its own duration, not a whole step.</summary>
    [TestClass]
    public sealed class ActionStepTests : TestBase
    {
        private static readonly WDateTime Start = WDateOnly.New(116, 1, 1).ToDateTime();

        private static PhysiologyState Nominal() => new(
            Energy: 70, SleepDebtHours: 2, Hunger: 40, Thirst: 60, Pain: 5, ImmuneLoad: 10, BodyTempDelta: 0, Cycle: null, Aging: new PhysicalAgingState());

        private static DefaultPhysiologyEngine Engine()
        {
            var cfg = Options.Create(new PhysiologyConfig(EnableMenstrualCycle: false, EnableTestosteroneCycle: false));
            var engine = new DefaultPhysiologyEngine(
                cfg, Options.Create(new MenstrualCycleConfig()), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)), new ZeroRandom(),
                biology: SexBiology.Female, birthDate: WDateOnly.New(86, 1, 1), now: WDateOnly.New(116, 1, 1));
            engine.RestoreState(Nominal() with { Aging = engine.State.Aging });
            return engine;
        }

        private static IHumanContext Context(PlannedAction? plan)
        {
            var snapshot = new EnginesSnapshot(
                Nominal(), new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral),
                new BehaviorState(40, 20, 15, 40, 50, 30, plan),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()),
                AmbientTemperature: 20.0);
            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4), Sociosexuality.Intermediate, Chronotype.Neutral),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        /// <summary>
        /// Thirst after a 10 minute plan chosen at <see cref="Start"/> and then half an hour of simulation, in steps of the given length.
        /// The plan reaches Physiology one step after it is chosen (Physiology runs before Behavior in the tick), is current for one decision, then replaced by Idle.
        /// </summary>
        private static double ThirstAfter(string action, int stepMinutes, int durationMinutes)
        {
            var engine = Engine();
            var outbox = new EventCollector();
            var step = WTimeSpan.FromMinutes(stepMinutes);
            var plan = new PlannedAction(action, Start, WTimeSpan.FromMinutes(durationMinutes), 50.0);
            var idle = new PlannedAction(Idle, Start, WTimeSpan.FromMinutes(30), 10.0);
            var now = Start + step;
            var end = Start + step + WTimeSpan.FromMinutes(30);
            var first = true;
            while (now < end)
            {
                engine.Tick(now, step, Context(first ? plan : idle), outbox);
                first = false;
                now += step;
            }

            return engine.State.Thirst;
        }

        [TestMethod]
        public void TenMinuteDrink_LowersThirstTheSame_WhateverTheStep()
        {
            var fine = ThirstAfter(ActionNames.Drink, 10, 10);
            var medium = ThirstAfter(ActionNames.Drink, 15, 10);
            var coarse = ThirstAfter(ActionNames.Drink, 30, 10);

            Assert.AreEqual(fine, medium, 1.0, $"10 min steps {fine:F1}, 15 min steps {medium:F1}");
            Assert.AreEqual(fine, coarse, 1.0, $"10 min steps {fine:F1}, 30 min steps {coarse:F1}");
        }

        [TestMethod]
        public void ThirtyMinuteMeal_LowersHungerTheSame_WhateverTheStep()
        {
            static double HungerAfter(int stepMinutes)
            {
                var engine = Engine();
                var outbox = new EventCollector();
                var step = WTimeSpan.FromMinutes(stepMinutes);
                var plan = new PlannedAction(ActionNames.Eat, Start, WTimeSpan.FromMinutes(30), 50.0);
                var idle = new PlannedAction(ActionNames.Idle, Start, WTimeSpan.FromMinutes(30), 10.0);
                var now = Start + step;
                var end = Start + step + WTimeSpan.FromMinutes(60);
                var elapsed = WTimeSpan.Zero;
                while (now < end)
                {
                    engine.Tick(now, step, Context(elapsed < WTimeSpan.FromMinutes(30) ? plan : idle), outbox);
                    elapsed += step;
                    now += step;
                }

                return engine.State.Hunger;
            }

            Assert.AreEqual(HungerAfter(10), HungerAfter(30), 1.5);
        }

        [TestMethod]
        public void WhatIsDrunk_RelievesThirstAtOnce_ButTheDeficitFallsOnlyAsTheStomachEmpties()
        {
            var cfg = new PhysiologyConfig();
            var engine = Engine();
            var outbox = new EventCollector();
            var step = WTimeSpan.FromMinutes(10);
            var plan = new PlannedAction(ActionNames.Drink, Start, WTimeSpan.FromMinutes(30), 50.0);
            var idle = new PlannedAction(ActionNames.Idle, Start, WTimeSpan.FromMinutes(30), 10.0);
            var deficitBefore = engine.State.Thirst * cfg.LitresPerThirstPoint;

            var now = Start + step;
            for (var i = 0; i < 3; i++)
            {
                engine.Tick(now, step, Context(plan), outbox);
                now += step;
            }

            var s = engine.State;
            Assert.IsTrue(s.Thirst < 40.0, $"Half an hour of drinking (25 points at 50 per hour) relieves thirst at once: {s.Thirst:F1}");
            Assert.IsTrue(s.GutWaterL > 0.3, $"Part of what was drunk is still in the stomach: {s.GutWaterL:F2} L");
            Assert.IsTrue(s.WaterDeficitL > deficitBefore - 0.6, $"The deficit has fallen only by what the stomach passed on (about 0.5 L): {deficitBefore:F2} to {s.WaterDeficitL:F2} L");

            for (var i = 0; i < 18; i++)
            {
                engine.Tick(now, step, Context(idle), outbox);
                now += step;
            }

            var later = engine.State;
            Assert.IsTrue(later.GutWaterL < 0.01, "After three hours the stomach is empty.");
            Assert.AreEqual(later.WaterDeficitL!.Value / cfg.LitresPerThirstPoint, later.Thirst, 1e-6, "With an empty stomach the thirst score is the deficit again.");
            var expected = deficitBefore - 25.0 * cfg.LitresPerThirstPoint + 3.5 * cfg.ThirstDriftAwakePerHour * cfg.LitresPerThirstPoint;
            Assert.AreEqual(expected, later.WaterDeficitL.Value, 0.15, "The drunk litres have all reached the body, less what was lost since.");
        }

        [TestMethod]
        public void LethalDeficit_IsTheTrueDeficit_NotTheThirstReliefOfWhatIsInTheStomach()
        {
            var cfg = new PhysiologyConfig();
            var dying = Nominal() with { WaterDeficitL = WaterMath.LethalDeficitL(cfg) + 0.5, GutWaterL = 3.0, Thirst = 0.0 };

            Assert.IsTrue(WaterMath.ViewThirst(dying.WaterDeficitL!.Value, dying.GutWaterL, cfg) >= 0.0);
            Assert.IsTrue(WaterMath.IsLethal(dying with { Thirst = WaterMath.ViewThirst(dying.WaterDeficitL!.Value, dying.GutWaterL, cfg) }, cfg), "Water not yet absorbed does not undo the lethal deficit.");
        }

        private const string Idle = ActionNames.Idle;
    }
}
