// DailyScheduleEngineTests.cs
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
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class DailyScheduleEngineTests : TestBase
    {
        // ── Section 1: OccupationScheduleSeeder ───────────────────────────────

        [TestMethod]
        public void Seed_Craftsperson_ReturnsExpectedSlots()
        {
            var personality = BuildPersonality();
            var slots = OccupationScheduleSeeder.Seed(OccupationKind.Craftsperson, personality);

            Assert.AreEqual(3, slots.Count);
            Assert.IsTrue(slots.Any(s => s.PreferredAction == Work));
            Assert.IsTrue(slots.Any(s => s.PreferredAction == ReachOut));
            Assert.IsTrue(slots.All(s => s.BiasStrength is >= 0.1 and <= 1.0));
        }

        [TestMethod]
        public void Seed_None_ReturnsEmptyList()
        {
            var personality = BuildPersonality();
            var slots = OccupationScheduleSeeder.Seed(OccupationKind.None, personality);

            Assert.AreEqual(0, slots.Count);
        }

        [TestMethod]
        public void Seed_EarlyBirdChronotype_SlotsShiftedEarlier()
        {
            var lark  = BuildPersonality(chronotype: Chronotype.Lark);
            var neutral = BuildPersonality(chronotype: Chronotype.Neutral);

            var larkSlots    = OccupationScheduleSeeder.Seed(OccupationKind.Craftsperson, lark);
            var neutralSlots = OccupationScheduleSeeder.Seed(OccupationKind.Craftsperson, neutral);

            for (var i = 0; i < larkSlots.Count; i++)
            {
                Assert.IsTrue(larkSlots[i].HourOfDay < neutralSlots[i].HourOfDay
                    || larkSlots[i].HourOfDay == 0,  // clamped at 0
                    $"Slot {i}: lark={larkSlots[i].HourOfDay} should be earlier than neutral={neutralSlots[i].HourOfDay}");
            }
        }

        [TestMethod]
        public void Seed_NightOwlChronotype_SlotsShiftedLater()
        {
            var owl     = BuildPersonality(chronotype: Chronotype.Owl);
            var neutral = BuildPersonality(chronotype: Chronotype.Neutral);

            var owlSlots     = OccupationScheduleSeeder.Seed(OccupationKind.Craftsperson, owl);
            var neutralSlots = OccupationScheduleSeeder.Seed(OccupationKind.Craftsperson, neutral);

            // At least some slots should be later
            Assert.IsTrue(Enumerable.Range(0, owlSlots.Count).Any(i => owlSlots[i].HourOfDay > neutralSlots[i].HourOfDay),
                "Owl slots should be shifted later than neutral");
        }

        [TestMethod]
        public void Seed_HighAffiliation_BoostsReachOutStrength()
        {
            var high    = BuildPersonality(affiliation: 0.9);
            var normal  = BuildPersonality(affiliation: 0.5);

            var highSlots   = OccupationScheduleSeeder.Seed(OccupationKind.Craftsperson, high);
            var normalSlots = OccupationScheduleSeeder.Seed(OccupationKind.Craftsperson, normal);

            var highReachOut   = highSlots.First(s => s.PreferredAction == ReachOut).BiasStrength;
            var normalReachOut = normalSlots.First(s => s.PreferredAction == ReachOut).BiasStrength;

            Assert.IsTrue(highReachOut > normalReachOut,
                $"High affiliation should boost ReachOut: high={highReachOut}, normal={normalReachOut}");
        }

        // ── Section 2: Slot scheduling ─────────────────────────────────────────

        [TestMethod]
        public void SeedFromOccupation_SchedulesTodaysSlots_ViaScheduler()
        {
            var engine     = BuildEngine();
            var self       = new HumanId(Guid.NewGuid());
            var personality = BuildPersonality();
            var now        = new WDateTime(0);
            var scheduler  = new RecordingScheduler();

            engine.SeedFromOccupation(OccupationKind.Craftsperson, personality, now, scheduler, self);

            Assert.AreEqual(3, scheduler.ScheduledCount, "Should have scheduled 3 slots for today");
        }

        [TestMethod]
        public void Tick_NewDay_ReschedulesTomorrowsSlots()
        {
            var config      = new DailyScheduleConfig(RescheduleLeadHours: 0.0);
            var engine      = BuildEngine(config);
            var self        = new HumanId(Guid.NewGuid());
            var personality = BuildPersonality();
            var day0        = new WDateTime(0);
            var scheduler   = new RecordingScheduler();

            engine.SeedFromOccupation(OccupationKind.Craftsperson, personality, day0, scheduler, self);
            var afterSeed = scheduler.ScheduledCount;  // 3 slots for today

            // Advance to next day — use the same RecordingScheduler in the context
            var day1 = day0 + WTimeSpan.FromHours(26);  // 26h per day in default calendar
            var ctx  = BuildContext(self, scheduler: scheduler);
            engine.Tick(day1, WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(scheduler.ScheduledCount > afterSeed,
                $"Tick on new day should schedule tomorrow's slots. Before={afterSeed}, After={scheduler.ScheduledCount}");
        }

        [TestMethod]
        public void Tick_SameDay_DoesNotReschedule()
        {
            var engine      = BuildEngine();
            var self        = new HumanId(Guid.NewGuid());
            var personality = BuildPersonality();
            var now         = new WDateTime(0);
            var scheduler   = new RecordingScheduler();

            engine.SeedFromOccupation(OccupationKind.Craftsperson, personality, now, scheduler, self);
            var afterSeed = scheduler.ScheduledCount;

            // Tick same day — RescheduleLeadHours=1.0, looking 1h ahead is still day 0
            var ctx = BuildContext(self, scheduler: scheduler);
            engine.Tick(now + WTimeSpan.FromHours(2), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.AreEqual(afterSeed, scheduler.ScheduledCount, "No rescheduling on same day");
        }

        // ── Section 3: Handle + ActiveSlot ────────────────────────────────────

        [TestMethod]
        public void Handle_ScheduleSlotTriggered_SetsActiveSlot()
        {
            var engine      = BuildEngine();
            var self        = new HumanId(Guid.NewGuid());
            var personality = BuildPersonality();
            var now         = new WDateTime(0);
            var scheduler   = new RecordingScheduler();

            engine.SeedFromOccupation(OccupationKind.Craftsperson, personality, now, scheduler, self);
            var slot   = engine.State.Slots.First();
            var outbox = new EventCollector();
            var ctx    = BuildContext(self);

            engine.Handle(new ScheduleSlotTriggered(now, self, slot.SlotId, slot.PreferredAction, null, slot.BiasStrength), ctx, outbox);

            Assert.IsNotNull(engine.State.ActiveSlot, "ActiveSlot should be set after Handle");
            Assert.AreEqual(slot.SlotId, engine.State.ActiveSlot!.SlotId);
        }

        [TestMethod]
        public void Tick_ClearsActiveSlotFromPreviousTick()
        {
            var engine  = BuildEngine();
            var self    = new HumanId(Guid.NewGuid());
            var now     = new WDateTime(0);
            var slot    = new ScheduleSlot("test_slot", 8, Work);
            var state   = new DailyScheduleState(new[] { slot }, slot, now.Date.DayIndex, OccupationKind.Craftsperson);
            engine.RestoreState(state);

            var ctx = BuildContext(self);
            engine.Tick(now + WTimeSpan.FromHours(1), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsNull(engine.State.ActiveSlot, "Tick should clear ActiveSlot from previous tick");
        }

        [TestMethod]
        public void Handle_UnknownSlotId_DoesNothing()
        {
            var engine = BuildEngine();
            var self   = new HumanId(Guid.NewGuid());
            var now    = new WDateTime(0);
            engine.RestoreState(new DailyScheduleState(
                new[] { new ScheduleSlot("real_slot", 8, Work) }, null, now.Date.DayIndex, OccupationKind.Craftsperson));

            engine.Handle(new ScheduleSlotTriggered(now, self, "nonexistent_slot", Work, null, 0.7), BuildContext(self), new EventCollector());

            Assert.IsNull(engine.State.ActiveSlot, "Unknown slot should not set ActiveSlot");
        }

        // ── Section 4: Behavior modifier ──────────────────────────────────────

        [TestMethod]
        public void Modifier_ActiveSlot_BoostsPreferredActionUtility()
        {
            var modifier = new DailyScheduleBehaviorModifier();
            var now      = new WDateTime(0);
            var slot     = new ScheduleSlot("work_slot", 8, Work, BiasStrength: 0.7);
            var context  = BuildBehaviorContext(
                new DailyScheduleState(new[] { slot }, slot, now.Date.DayIndex, OccupationKind.Craftsperson),
                stress: 20.0, energy: 80.0);

            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),
                new(Idle, 30.0, WTimeSpan.FromMinutes(30), BehaviorDomain.Physiological)
            };

            modifier.Modify(context, candidates);

            var work = candidates.First(c => c.Name == Work);
            var idle = candidates.First(c => c.Name == Idle);
            Assert.IsTrue(work.Utility > 50.0, $"Work should be boosted: {work.Utility}");
            Assert.AreEqual(30.0, idle.Utility, 0.001, "Idle should not be boosted");
        }

        [TestMethod]
        public void Modifier_NoActiveSlot_NoBiasApplied()
        {
            var modifier = new DailyScheduleBehaviorModifier();
            var now      = new WDateTime(0);
            var context  = BuildBehaviorContext(
                new DailyScheduleState(Array.Empty<ScheduleSlot>(), null, now.Date.DayIndex, OccupationKind.None),
                stress: 10.0, energy: 80.0);

            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence)
            };
            var before = candidates[0].Utility;

            modifier.Modify(context, candidates);

            Assert.AreEqual(before, candidates[0].Utility, 0.001, "No bias should apply without active slot");
        }

        [TestMethod]
        public void Modifier_HighStress_SkipWhenStressedSlot_NoBias()
        {
            var modifier = new DailyScheduleBehaviorModifier();
            var now      = new WDateTime(0);
            var slot     = new ScheduleSlot("work_slot", 8, Work, CanSkipWhenStressed: true);
            var context  = BuildBehaviorContext(
                new DailyScheduleState(new[] { slot }, slot, now.Date.DayIndex, OccupationKind.Craftsperson),
                stress: 85.0, energy: 80.0);  // stress above threshold

            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence)
            };
            var before = candidates[0].Utility;

            modifier.Modify(context, candidates);

            Assert.AreEqual(before, candidates[0].Utility, 0.001, "Skippable slot should not bias under high stress");
        }

        [TestMethod]
        public void Modifier_LowEnergy_SkipWhenStressedSlot_NoBias()
        {
            var modifier = new DailyScheduleBehaviorModifier();
            var now      = new WDateTime(0);
            var slot     = new ScheduleSlot("work_slot", 8, Work, CanSkipWhenStressed: true);
            var context  = BuildBehaviorContext(
                new DailyScheduleState(new[] { slot }, slot, now.Date.DayIndex, OccupationKind.Craftsperson),
                stress: 20.0, energy: 10.0);  // energy below threshold

            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence)
            };
            var before = candidates[0].Utility;

            modifier.Modify(context, candidates);

            Assert.AreEqual(before, candidates[0].Utility, 0.001, "Skippable slot should not bias under low energy");
        }

        [TestMethod]
        public void Modifier_HighStress_NonSkippableSlot_BiasApplied()
        {
            var modifier = new DailyScheduleBehaviorModifier();
            var now      = new WDateTime(0);
            var slot     = new ScheduleSlot("social_slot", 19, ReachOut, CanSkipWhenStressed: false);
            var context  = BuildBehaviorContext(
                new DailyScheduleState(new[] { slot }, slot, now.Date.DayIndex, OccupationKind.Craftsperson),
                stress: 85.0, energy: 80.0);  // high stress, but not skippable

            var candidates = new List<BehaviorCandidate>
            {
                new(ReachOut, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };
            var before = candidates[0].Utility;

            modifier.Modify(context, candidates);

            Assert.IsTrue(candidates[0].Utility > before, "Non-skippable slot should bias even under stress");
        }

        // ── Section 5: Integration ─────────────────────────────────────────────

        [TestMethod]
        public void SeedAndTick_CraftsmanCharacter_WorkBiasedInMorning()
        {
            var engine      = BuildEngine();
            var self        = new HumanId(Guid.NewGuid());
            var personality = BuildPersonality();
            var now         = new WDateTime(0);
            var scheduler   = new RecordingScheduler();

            engine.SeedFromOccupation(OccupationKind.Craftsperson, personality, now, scheduler, self);

            // Simulate the morning Work slot firing
            var workSlot = engine.State.Slots.First(s => s.PreferredAction == Work);
            var outbox   = new EventCollector();
            var ctx      = BuildContext(self);
            engine.Handle(new ScheduleSlotTriggered(now, self, workSlot.SlotId, workSlot.PreferredAction, null, workSlot.BiasStrength), ctx, outbox);

            Assert.IsNotNull(engine.State.ActiveSlot);
            Assert.AreEqual(Work, engine.State.ActiveSlot!.PreferredAction);
        }

        [TestMethod]
        public void SeedAndTick_GuardCharacter_NightOwlShift_AdjustedHours()
        {
            var owl         = BuildPersonality(chronotype: Chronotype.Owl);
            var neutral     = BuildPersonality(chronotype: Chronotype.Neutral);

            var owlSlots     = OccupationScheduleSeeder.Seed(OccupationKind.Guard, owl);
            var neutralSlots = OccupationScheduleSeeder.Seed(OccupationKind.Guard, neutral);

            // Night owl guard should have later slots
            Assert.IsTrue(
                owlSlots.Sum(s => s.HourOfDay) >= neutralSlots.Sum(s => s.HourOfDay),
                "Owl guard should have equal or later hours than neutral guard");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static DefaultDailyScheduleEngine BuildEngine(DailyScheduleConfig? config = null)
        {
            var cfg = Options.Create(config ?? new DailyScheduleConfig());
            var log = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
                                   .CreateLogger<DefaultDailyScheduleEngine>();
            return new DefaultDailyScheduleEngine(cfg, log);
        }

        private static Personality BuildPersonality(
            double affiliation = 0.5,
            double competence  = 0.5,
            double openness    = 0.5,
            Chronotype chronotype = Chronotype.Neutral)
            => new(
                new BigFive(openness, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(affiliation, 0.5, 0.3, 0.4, competence, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                chronotype);

        private static IHumanContext BuildContext(HumanId self, DailyScheduleState? schedule = null,
            double stress = 20.0, double energy = 80.0, IScheduler? scheduler = null)
        {
            var personality = BuildPersonality();
            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "A", Familiar = new[] { "A" } },
                    new Surname { Male = "B", Female = "B" },
                    WDateOnly.New(100, 1, 1)),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = new EnginesSnapshot(
                    new PhysiologyState(energy, 0, 5, 5, 0, 0, 0, null),
                    new PsychologyState(0, 0.5, 0.5, stress, 0, DiscreteEmotion.Neutral),
                    new BehaviorState(10, 5, 5, 20, 50, 30, null),
                    new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                    new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                    new MemoryIndex(new List<EpisodicMemory>()),
                    SemanticMemoryState.Empty,
                    Schedule: schedule),
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("ScheduleTests"),
                EventBus = new NullEventBus(),
                Scheduler = scheduler ?? new NullScheduler()
            };
        }

        private static BehaviorContext BuildBehaviorContext(
            DailyScheduleState schedule,
            double stress = 20.0,
            double energy = 80.0)
        {
            var human = BuildContext(new HumanId(Guid.NewGuid()), schedule, stress, energy);
            var state = new BehaviorState(10, 5, 5, 20, 50, 30, null);
            return new BehaviorContext(
                new WDateTime(0),
                WTimeSpan.FromHours(1),
                human,
                new EventCollector(),
                state,
                new BehaviorConfig(),
                new Dictionary<string, double>());
        }

        /// <summary>
        /// Test double that records how many times ScheduleAt was called.
        /// </summary>
        private sealed class RecordingScheduler : IScheduler
        {
            public int ScheduledCount { get; private set; }

            public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
            {
                ScheduledCount++;
                return new ScheduledId(Guid.NewGuid());
            }

            public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
            {
                ScheduledCount++;
                return new ScheduledId(Guid.NewGuid());
            }

            public bool Cancel(ScheduledId id) => true;

            public IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime now)
                => Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }
    }
}
