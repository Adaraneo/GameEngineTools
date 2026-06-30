// BereavementTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Bereavement;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Tests for the bereavement subsystem: trajectory-class prevalences (Lundorff 2020), grief
    /// intensity scaling with the lost bond, the DPM "waves of grief" oscillation (no stage automaton),
    /// the acute grief spike into Psychology, ritual relief, and the widowhood mortality hazard.
    /// </summary>
    [TestClass]
    public class BereavementTests : TestBase
    {
        private static readonly BereavementConfig Cfg = new();

        #region Trajectory prevalences — Lundorff 2020 ~64/20/8/7

        [TestMethod]
        public void AssignTrajectory_ApproximatesEmpiricalPrevalences()
        {
            var rng = new SystemRandomSource(12345);
            var counts = new Dictionary<GriefTrajectory, int>
            {
                [GriefTrajectory.Resilient] = 0,
                [GriefTrajectory.ModerateStable] = 0,
                [GriefTrajectory.Recovery] = 0,
                [GriefTrajectory.Prolonged] = 0,
            };

            const int n = 20000;
            for (var i = 0; i < n; i++)
                counts[BereavementMath.AssignTrajectory(rng, Cfg, attachmentAnxiety: 0.0, violent: false)]++;

            Assert.AreEqual(0.644, counts[GriefTrajectory.Resilient] / (double)n, 0.03, "Resilient ~64%.");
            Assert.AreEqual(0.204, counts[GriefTrajectory.ModerateStable] / (double)n, 0.03, "ModerateStable ~20%.");
            Assert.AreEqual(0.084, counts[GriefTrajectory.Recovery] / (double)n, 0.02, "Recovery ~8%.");
            Assert.AreEqual(0.068, counts[GriefTrajectory.Prolonged] / (double)n, 0.02, "Prolonged ~7%.");
        }

        [TestMethod]
        public void AssignTrajectory_ViolentDeath_RaisesProlongedShare()
        {
            var rng = new SystemRandomSource(999);
            const int n = 20000;
            var nonViolentProlonged = 0;
            var violentProlonged = 0;

            for (var i = 0; i < n; i++)
            {
                if (BereavementMath.AssignTrajectory(rng, Cfg, 0.0, violent: false) == GriefTrajectory.Prolonged)
                    nonViolentProlonged++;
                if (BereavementMath.AssignTrajectory(rng, Cfg, 0.0, violent: true) == GriefTrajectory.Prolonged)
                    violentProlonged++;
            }

            Assert.IsTrue(violentProlonged > nonViolentProlonged * 2,
                $"Violent/sudden loss sharply raises prolonged-grief risk. non-violent={nonViolentProlonged}, violent={violentProlonged}");
        }

        #endregion

        #region Onset intensity ∝ bond, partner > acquaintance

        [TestMethod]
        public void OnsetIntensity_PartnerLoss_HurtsMoreThanAcquaintance()
        {
            var partner = BereavementMath.OnsetIntensity(bondStrength: 80, KinRole.Partner, Cfg);
            var acquaintance = BereavementMath.OnsetIntensity(bondStrength: 80, KinRole.None, Cfg);
            Assert.IsTrue(partner > acquaintance, $"A partner loss is more intense. partner={partner:F1}, other={acquaintance:F1}");

            var weak = BereavementMath.OnsetIntensity(bondStrength: 20, KinRole.None, Cfg);
            Assert.IsTrue(acquaintance > weak, "Intensity scales with the strength of the lost bond.");
        }

        #endregion

        #region DPM — waves of grief, not a monotonic decline

        [TestMethod]
        public void LoRoOscillation_ProducesWaves_NotMonotonicDecay()
        {
            var rose = false;
            var fell = false;
            var prev = BereavementMath.LoRoOscillation(0.0, Cfg);

            for (var day = 0.25; day <= 14.0; day += 0.25)
            {
                var cur = BereavementMath.LoRoOscillation(day, Cfg);
                if (cur > prev + 1e-6) rose = true;
                if (cur < prev - 1e-6) fell = true;
                prev = cur;
            }

            Assert.IsTrue(rose && fell, "Grief oscillates (loss/restoration waves) rather than decaying monotonically.");
        }

        #endregion

        #region Engine — onset registers a loss + acute spike; idempotent

        [TestMethod]
        public void Handle_BereavementOnset_RegistersLoss_AssignsTrajectory_EmitsAcutePang()
        {
            var self = NewId();
            var deceased = NewId();
            var engine = NewEngine();
            var ctx = BuildContext(self);
            var outbox = new EventCollector();

            engine.Handle(
                new BereavementOnset(new WDateTime(0), self, deceased, BondStrength: 80, DeathCause.OldAge, KinRole.Partner),
                ctx, outbox);

            Assert.AreEqual(1, engine.State.Losses.Count, "A loss record is created.");
            Assert.AreEqual(deceased, engine.State.Losses[0].DeceasedId);

            var events = outbox.Drain();
            Assert.IsTrue(events.OfType<GriefTrajectoryAssigned>().Any(), "A trajectory is assigned.");
            var pang = events.OfType<GriefPang>().SingleOrDefault();
            Assert.IsNotNull(pang, "The acute grief spike is emitted as a pang.");
            Assert.IsTrue(pang!.ValenceDelta < 0 && pang.MoodBaselineDelta < 0 && pang.StressDelta > 0,
                "The acute pang drops valence + mood baseline and raises stress.");
        }

        [TestMethod]
        public void Handle_BereavementOnset_IsIdempotentPerDeceased()
        {
            var self = NewId();
            var deceased = NewId();
            var engine = NewEngine();
            var ctx = BuildContext(self);

            var onset = new BereavementOnset(new WDateTime(0), self, deceased, 80, DeathCause.OldAge, KinRole.Partner);
            engine.Handle(onset, ctx, new EventCollector());
            engine.Handle(onset, ctx, new EventCollector());

            Assert.AreEqual(1, engine.State.Losses.Count, "Re-delivering the same death does not duplicate the loss.");
        }

        [TestMethod]
        public void Handle_FuneralHeld_ReducesGriefIntensity()
        {
            var self = NewId();
            var deceased = NewId();
            var engine = NewEngine();
            var ctx = BuildContext(self);

            engine.Handle(new BereavementOnset(new WDateTime(0), self, deceased, 80, DeathCause.OldAge, KinRole.Partner), ctx, new EventCollector());
            var before = engine.State.Losses[0].GriefIntensity;

            engine.Handle(new FuneralHeld(new WDateTime(0), self, deceased, Attendees: 4), ctx, new EventCollector());
            var after = engine.State.Losses[0].GriefIntensity;

            Assert.IsTrue(after < before, $"A funeral relieves grief (regained control/closure). before={before:F1}, after={after:F1}");
        }

        #endregion

        #region Widowhood mortality hazard

        [TestMethod]
        public void Widowhood_PartnerLoss_RaisesHazard_StrongerEarly_AndForMen()
        {
            var now = new WDateTime(0) + WTimeSpan.FromDays(30); // 30 days after the loss
            var loss = new LossRecord(NewId(), KinRole.Partner, 80, new WDateTime(0),
                GriefTrajectory.ModerateStable, 60, 1.0, ContinuingBond.None, false);
            var state = new BereavementState(new[] { loss });

            var female = BereavementMath.WidowhoodHazardMultiplier(state, SexBiology.Female, now, Cfg);
            var male = BereavementMath.WidowhoodHazardMultiplier(state, SexBiology.Male, now, Cfg);

            Assert.AreEqual(Cfg.WidowhoodHazardFirst, female, 1e-6, "Acute window uses the first-window multiplier.");
            Assert.IsTrue(male > female, "Male survivors fare worse (Shor 2012).");
        }

        [TestMethod]
        public void Widowhood_DecaysToTail_ThenToBaseline()
        {
            var loss0 = new WDateTime(0);
            var loss = new LossRecord(NewId(), KinRole.Partner, 80, loss0,
                GriefTrajectory.ModerateStable, 60, 1.0, ContinuingBond.None, false);
            var state = new BereavementState(new[] { loss });

            var tail = BereavementMath.WidowhoodHazardMultiplier(state, SexBiology.Female, loss0 + WTimeSpan.FromDays(365), Cfg);
            var resolved = BereavementMath.WidowhoodHazardMultiplier(state, SexBiology.Female, loss0 + WTimeSpan.FromDays(900), Cfg);

            Assert.AreEqual(Cfg.WidowhoodHazardTail, tail, 1e-6, "Mid-term uses the tail multiplier.");
            Assert.AreEqual(1.0, resolved, 1e-6, "Beyond the tail window the hazard returns to baseline.");
        }

        [TestMethod]
        public void Widowhood_NonPartnerLoss_HasNoHazard()
        {
            var now = new WDateTime(0) + WTimeSpan.FromDays(10);
            var loss = new LossRecord(NewId(), KinRole.Sibling, 80, new WDateTime(0),
                GriefTrajectory.ModerateStable, 60, 1.0, ContinuingBond.None, false);
            var state = new BereavementState(new[] { loss });

            Assert.AreEqual(1.0, BereavementMath.WidowhoodHazardMultiplier(state, SexBiology.Male, now, Cfg), 1e-6,
                "The widowhood effect is partner-specific.");
        }

        #endregion

        #region Psychology integration — grief pang drops mood, sets Sadness

        [TestMethod]
        public void Psychology_GriefPang_DropsMoodBaseline_AndSetsSadness()
        {
            var id = NewId();
            var ctx = BuildPsychContext(id);
            var engine = new DefaultPsychologyEngine(Options.Create(new PsychologyConfig()), Loggers(), new ZeroRandomSource());
            var before = engine.State.MoodBaseline;

            engine.Handle(
                new GriefPang(new WDateTime(0), id, NewId(), Intensity: 80, ValenceDelta: -0.7, MoodBaselineDelta: -20, StressDelta: 25),
                ctx, new EventCollector());

            Assert.IsTrue(engine.State.MoodBaseline < before, "A grief pang lowers the persistent mood baseline.");
            Assert.AreEqual(DiscreteEmotion.Sadness, engine.State.DominantEmotion, "Grief presents as Sadness.");
            Assert.IsTrue(engine.State.Stress > 20, "A grief pang raises stress.");
        }

        #endregion

        #region Physical burial — Buried / GraveVisited handlers

        [TestMethod]
        public void Handle_Buried_MarksLossRecordBuried()
        {
            var self = NewId();
            var deceased = NewId();
            var engine = NewEngine();
            var ctx = BuildContext(self);

            engine.Handle(new BereavementOnset(new WDateTime(0), self, deceased, 80, DeathCause.OldAge, KinRole.Partner), ctx, new EventCollector());
            Assert.IsFalse(engine.State.Losses[0].Buried);

            engine.Handle(new GameEngineTools.Characters.Engines.Bereavement.Buried(new WDateTime(0), self, deceased), ctx, new EventCollector());
            Assert.IsTrue(engine.State.Losses[0].Buried, "Burial marks the loss record as buried.");
        }

        [TestMethod]
        public void Handle_GraveVisited_RelievesGrief_AndInternalisesBond()
        {
            var self = NewId();
            var deceased = NewId();
            var engine = NewEngine();
            var ctx = BuildContext(self);

            engine.Handle(new BereavementOnset(new WDateTime(0), self, deceased, 80, DeathCause.OldAge, KinRole.Partner), ctx, new EventCollector());
            var before = engine.State.Losses[0].GriefIntensity;
            Assert.AreEqual(ContinuingBond.None, engine.State.Losses[0].Bond);

            engine.Handle(new GameEngineTools.Characters.Engines.Bereavement.GraveVisited(new WDateTime(0), self, deceased), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Losses[0].GriefIntensity < before, "A grave visit gives closure (small grief relief).");
            Assert.AreEqual(ContinuingBond.Internalized, engine.State.Losses[0].Bond, "A tended grave internalises the continuing bond.");
        }

        #endregion

        #region BurialObjects helper — id round-trip

        [TestMethod]
        public void BurialObjects_EncodeAndRecoverDeceasedId()
        {
            var deceased = NewId();
            var corpse = GameEngineTools.World.Objects.BurialObjects.Corpse(deceased, "graveyard", "Old Tom");
            var grave = GameEngineTools.World.Objects.BurialObjects.Grave(deceased, "graveyard", "Old Tom");

            Assert.AreEqual(GameEngineTools.World.Objects.WorldObjectCategory.Corpse, corpse.Category);
            Assert.AreEqual(GameEngineTools.World.Objects.WorldObjectCategory.Grave, grave.Category);

            Assert.IsTrue(GameEngineTools.World.Objects.BurialObjects.TryGetDeceased(corpse, out var fromCorpse) && fromCorpse == deceased);
            Assert.IsTrue(GameEngineTools.World.Objects.BurialObjects.TryGetDeceased(grave, out var fromGrave) && fromGrave == deceased);
        }

        #endregion

        #region Helpers

        private static HumanId NewId() => new(Guid.NewGuid());

        private static ILoggerFactory Loggers() => LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

        private static DefaultBereavementEngine NewEngine()
            => new DefaultBereavementEngine(Options.Create(Cfg), Loggers());

        private static Personality BuildPersonality()
            => new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

        private static EnginesSnapshot BuildSnapshot()
            => new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.5, 0.5, 20, 20, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, 0.2, SurfaceKind.Unknown, null),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

        private static IHumanContext BuildContext(HumanId id)
        {
            var personality = BuildPersonality();
            return new HumanContext
            {
                Id = id,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = BuildSnapshot(),
                Random = new SystemRandomSource(7),
                Logger = Loggers().CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private static IHumanContext BuildPsychContext(HumanId id)
        {
            var personality = BuildPersonality();
            return new HumanContext
            {
                Id = id,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = BuildSnapshot(),
                Random = new ZeroRandomSource(),
                Logger = Loggers().CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private sealed class ZeroRandomSource : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit() => 0.0;
            public bool Chance(double p) => false;
        }

        private sealed class SystemRandomSource : IRandomSource
        {
            private readonly Random _r;
            public SystemRandomSource(int seed) => _r = new Random(seed);
            public int Next(int min, int max) => _r.Next(min, max);
            public double NextUnit() => _r.NextDouble();
            public bool Chance(double p) => _r.NextDouble() < p;
        }

        #endregion
    }
}
