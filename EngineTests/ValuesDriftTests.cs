// ValuesDriftTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Values;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for R4 value drift: <see cref="DefaultValuesEngine"/> evolving
    /// <see cref="ValuesState.Current"/> from lived experience while regressing toward
    /// the immutable <see cref="ValuesState.Baseline"/>.
    /// </summary>
    [TestClass]
    public class ValuesDriftTests : TestBase
    {
        #region Test 1 — repeated violation erodes the held value monotonically

        [TestMethod]
        public void ValuesDrift_RepeatedViolation_ErodesHeldValue()
        {
            var baseline = Neutral();
            var (engine, ctx, self) = MakeEngine(baseline);
            // Start with a high Benevolence so there is room to erode.
            engine.RestoreState(new ValuesState(baseline with { Benevolence = 0.95 }, baseline));

            var outbox = new EventCollector();
            var prev = engine.State.Current.Benevolence;
            var monotonic = true;

            for (var i = 0; i < 50; i++)
            {
                var ev = new ValueCongruenceViolated(
                    OccurredAt: At(100),
                    Actor: self,
                    ActionName: ActionNames.Work,
                    Congruence: -0.5,
                    DominantViolatedValue: nameof(ValuesProfile.Benevolence));

                engine.Handle(ev, ctx, outbox);

                var now = engine.State.Current.Benevolence;
                if (now > prev + 1e-9) monotonic = false;
                prev = now;
            }

            Assert.IsTrue(monotonic, "Benevolence must decrease monotonically under repeated violation.");
            Assert.IsTrue(engine.State.Current.Benevolence < 0.95 - 0.2,
                $"Benevolence must drop substantially. Got: {engine.State.Current.Benevolence:F3}");
            // Baseline must be untouched.
            Assert.AreEqual(baseline.Benevolence, engine.State.Baseline.Benevolence, 1e-9,
                "Baseline must never change from drift.");
        }

        #endregion

        #region Test 2 — no stimulation regresses toward baseline

        [TestMethod]
        public void ValuesDrift_NoStimulation_RegressesToBaseline()
        {
            var baseline = Neutral();
            var (engine, ctx, _) = MakeEngine(baseline);

            // Push Current away from Baseline, then let it regress with no events.
            var driftedBenevolence = Math.Clamp(baseline.Benevolence - 0.30, 0, 1);
            engine.RestoreState(new ValuesState(baseline with { Benevolence = driftedBenevolence }, baseline));

            var gapBefore = Math.Abs(engine.State.Current.Benevolence - baseline.Benevolence);

            var outbox = new EventCollector();
            // One year of game time, no events.
            engine.Tick(At(100), WTimeSpan.FromDays(365), ctx, outbox);

            var gapAfter = Math.Abs(engine.State.Current.Benevolence - baseline.Benevolence);

            Assert.IsTrue(gapAfter < gapBefore,
                $"Gap to baseline must shrink. Before={gapBefore:F4}, After={gapAfter:F4}");
            // ~8 %/year regression (Vecchione 2016): roughly 5–15 % of the gap closes in a year.
            var closedFraction = (gapBefore - gapAfter) / gapBefore;
            Assert.IsTrue(closedFraction is > 0.03 and < 0.20,
                $"Yearly regression should be slow (~8%). Closed fraction: {closedFraction:F3}");
        }

        #endregion

        #region Test 3 — external incentive discounts the update (Kelley discounting)

        [TestMethod]
        public void ValuesDrift_ExternalIncentive_DiscountsUpdate()
        {
            var baseline = Neutral();

            // Free (intrinsic) action.
            var (freeEngine, freeCtx, freeSelf) = MakeEngine(baseline);
            var freeBefore = freeEngine.State.Current.SelfDirection;
            freeEngine.Handle(
                new ActionCommitted(At(100), freeSelf, ActionNames.Create, WTimeSpan.FromHours(1)),
                freeCtx, new EventCollector());
            var freeDelta = freeEngine.State.Current.SelfDirection - freeBefore;

            // Externally-caused action (committed under conflict displacement → external cause).
            var (extEngine, extCtx, extSelf) = MakeEngine(baseline);
            var extBefore = extEngine.State.Current.SelfDirection;
            extEngine.Handle(
                new ActionCommitted(At(100), extSelf, ActionNames.Create, WTimeSpan.FromHours(1),
                    ConflictReason: "displaced-by-arbitration"),
                extCtx, new EventCollector());
            var extDelta = extEngine.State.Current.SelfDirection - extBefore;

            Assert.IsTrue(freeDelta > 0, $"Free affirming action must strengthen the value. Got: {freeDelta:F4}");
            Assert.IsTrue(extDelta > 0, $"Externally-caused action must still strengthen, weakly. Got: {extDelta:F4}");
            Assert.IsTrue(extDelta < freeDelta, "External incentive must discount the update.");

            // Discount factor ≈ 0.4 (default ExternalIncentiveDiscount).
            var ratio = extDelta / freeDelta;
            Assert.IsTrue(Math.Abs(ratio - 0.4) < 0.08,
                $"External update should be ~0.4× the free update. Ratio: {ratio:F3}");
        }

        #endregion

        #region Test 4 — guilt is keyed to Current, not Baseline

        [TestMethod]
        public void Guilt_KeyedToCurrentNotBaseline()
        {
            // Baseline values Conformity low; Current has drifted to value Conformity highly.
            var baseline = Neutral() with { Conformity = 0.10, Hedonism = 0.9, Benevolence = 0.9, Stimulation = 0.9 };
            var current  = Neutral() with { Conformity = 0.95, Hedonism = 0.05, Benevolence = 0.1, Stimulation = 0.1 };

            // InviteIntimacy violates Conformity/Tradition. With high-Conformity Current, congruence is negative.
            var loading = ActionValueLoadings.Get(ActionNames.InviteIntimacy);
            var congruenceCurrent  = loading.Congruence(current);
            var congruenceBaseline = loading.Congruence(baseline);

            var self = new HumanId(Guid.NewGuid());
            var outbox = new EventCollector();
            var ctx = BuildBehaviorContext(self, new ValuesState(current, baseline), stress: 10, cogLoad: 20, outbox);
            var candidates = new List<BehaviorCandidate>
            {
                new(ActionNames.InviteIntimacy, 50.0, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };

            new ValuesBehaviorModifier().Modify(ctx, candidates);

            var violation = outbox.Drain().OfType<ValueCongruenceViolated>().FirstOrDefault();
            Assert.IsNotNull(violation, "A value violation must be detected from the drifted Current profile.");

            // The emitted congruence must match Current, not Baseline.
            Assert.AreEqual(congruenceCurrent, violation!.Congruence, 1e-6,
                "Congruence must be computed from Current.");
            Assert.AreNotEqual(congruenceBaseline, violation.Congruence,
                "Congruence must NOT be computed from Baseline.");
        }

        #endregion

        #region Test 5 — acceptance: divergent choices produce distinct Current profiles

        [TestMethod]
        public void ValuesDrift_DivergentChoices_ProduceDistinctCurrentProfiles()
        {
            var baseline = Neutral();

            // Character A: repeatedly acts against Benevolence (selfish path).
            var (selfish, selfishCtx, selfishSelf) = MakeEngine(baseline);
            // Character B: repeatedly affirms Benevolence (prosocial path).
            var (prosocial, prosocialCtx, prosocialSelf) = MakeEngine(baseline);

            var box = new EventCollector();
            for (var i = 0; i < 40; i++)
            {
                selfish.Handle(new ValueCongruenceViolated(At(100), selfishSelf, ActionNames.Work, -0.5,
                    nameof(ValuesProfile.Benevolence)), selfishCtx, box);

                prosocial.Handle(new ActionCommitted(At(100), prosocialSelf, ActionNames.ReachOut, WTimeSpan.FromHours(1)),
                    prosocialCtx, box);
            }

            var distance = CosineDistance(selfish.State.Current, prosocial.State.Current);
            Assert.IsTrue(distance > 0.01,
                $"Divergent choices must produce measurably distinct Current profiles. Cosine distance: {distance:F4}");

            // Baselines stayed identical.
            Assert.AreEqual(
                CosineDistance(selfish.State.Baseline, prosocial.State.Baseline), 0.0, 1e-9,
                "Baselines must remain identical regardless of choices.");

            // The directional signature is correct: selfish eroded Benevolence, prosocial raised it.
            Assert.IsTrue(selfish.State.Current.Benevolence < prosocial.State.Current.Benevolence,
                "Selfish character must value Benevolence less than the prosocial character.");
        }

        #endregion

        #region Test 6 — routine actions over many years do not saturate (saturation fix)

        [TestMethod]
        public void ValuesDrift_RoutineActionsOverManyYears_DoNotSaturate()
        {
            var baseline = Neutral();
            var (engine, ctx, self) = MakeEngine(baseline);

            var outbox = new EventCollector();
            var dt = WTimeSpan.FromDays(1);
            var start = At(100);

            // 10 game years of a thin, broadly-loaded routine action (Sleep), committed every day,
            // with daily regression. Before the fix this saturated several dimensions to the clamp
            // via the unconditioned affirmation channel + circumplex coupling.
            for (var day = 0; day < 3650; day++)
            {
                var now = start + WTimeSpan.FromDays(day);
                engine.Handle(
                    new ActionCommitted(now, self, ActionNames.Sleep, WTimeSpan.FromHours(8)), ctx, outbox);
                engine.Tick(now, dt, ctx, outbox);
            }

            var cur = ToArray(engine.State.Current);
            var baseArr = ToArray(baseline);
            for (var i = 0; i < cur.Length; i++)
            {
                Assert.IsTrue(Math.Abs(cur[i] - baseArr[i]) <= 0.25,
                    $"Routine actions must not drive value drift to saturation. " +
                    $"Dim {i}: current={cur[i]:F3}, baseline={baseArr[i]:F3}");
            }
        }

        #endregion

        #region Test 7 — per-dimension cooldown caps same-day affirmations (saturation fix)

        [TestMethod]
        public void ValuesDrift_CooldownPreventsMultipleNudgesPerDay()
        {
            var baseline = Neutral();
            var (engine, ctx, self) = MakeEngine(baseline);
            var before = engine.State.Current.SelfDirection;

            // Commit the same value-charged action 20× within a single instant (same day).
            var now = At(100);
            for (var i = 0; i < 20; i++)
                engine.Handle(new ActionCommitted(now, self, ActionNames.Create, WTimeSpan.FromHours(1)),
                    ctx, new EventCollector());

            var afterSameDay = engine.State.Current.SelfDirection;
            Assert.AreEqual(0.02, afterSameDay - before, 1e-9,
                "Cooldown must cap same-day affirmations of one dimension to a single LearningRate nudge.");

            // Once the cooldown window elapses, the dimension can be affirmed again.
            var later = now + WTimeSpan.FromDays(2);
            engine.Handle(new ActionCommitted(later, self, ActionNames.Create, WTimeSpan.FromHours(1)),
                ctx, new EventCollector());
            Assert.IsTrue(engine.State.Current.SelfDirection > afterSameDay,
                "A fresh nudge must land once the affirmation cooldown expires.");
        }

        #endregion

        #region Helpers

        private static double[] ToArray(ValuesProfile p) => new[]
        {
            p.SelfDirection, p.Stimulation, p.Hedonism, p.Achievement, p.Power,
            p.Security, p.Conformity, p.Tradition, p.Benevolence, p.Universalism
        };

        private static ValuesProfile Neutral()
            => new ValuesProfile(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

        private static WDateTime At(int year) => WDateOnly.New(year, 1, 1).ToDateTime();

        private (DefaultValuesEngine engine, IHumanContext ctx, HumanId self) MakeEngine(
            ValuesProfile baseline, int birthYear = 80)
        {
            var self = new HumanId(Guid.NewGuid());
            var engine = new DefaultValuesEngine(
                Options.Create(new ValuesConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultValuesEngine>());
            engine.SeedFromBaseline(baseline);

            var ctx = BuildContext(self, birthYear);
            return (engine, ctx, self);
        }

        private static IHumanContext BuildContext(HumanId self, int birthYear)
        {
            var personality = MakePersonality();
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
                    new Name { Original = "Test", Familiar = new[] { "Test" } },
                    new Surname { Male = "Human", Female = "Human" },
                    WDateOnly.New(birthYear, 1, 1)),
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

        private static BehaviorContext BuildBehaviorContext(
            HumanId self, ValuesState values, double stress, double cogLoad, EventCollector outbox)
        {
            var personality = MakePersonality();
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.0, 0.4, 0.5, stress, cogLoad, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()),
                Values: values);

            var ctx = new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "Test", Familiar = new[] { "Test" } },
                    new Surname { Male = "Human", Female = "Human" },
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

            return new BehaviorContext(
                Now: At(100),
                Dt: WTimeSpan.FromHours(1),
                HumanContext: ctx,
                Outbox: outbox,
                State: new BehaviorState(10, 5, 5, 20, 50, 30, null),
                Config: new BehaviorConfig(),
                Cooldowns: new Dictionary<string, double>());
        }

        private static Personality MakePersonality()
            => new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

        private static double CosineDistance(ValuesProfile a, ValuesProfile b)
        {
            double[] av =
            {
                a.SelfDirection, a.Stimulation, a.Hedonism, a.Achievement, a.Power,
                a.Security, a.Conformity, a.Tradition, a.Benevolence, a.Universalism
            };
            double[] bv =
            {
                b.SelfDirection, b.Stimulation, b.Hedonism, b.Achievement, b.Power,
                b.Security, b.Conformity, b.Tradition, b.Benevolence, b.Universalism
            };

            double dot = 0, na = 0, nb = 0;
            for (var i = 0; i < av.Length; i++)
            {
                dot += av[i] * bv[i];
                na += av[i] * av[i];
                nb += bv[i] * bv[i];
            }

            if (na == 0 || nb == 0) return 0;
            var cos = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
            return 1.0 - cos;
        }

        #endregion
    }
}
