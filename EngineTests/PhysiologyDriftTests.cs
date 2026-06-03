// PhysiologyDriftTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Engines.Physiology;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Exhaustive unit tests for <see cref="DefaultPhysiologyEngine.ComputeDrift"/> — the single
    /// source of truth for baseline action-driven physiological drift. Each <c>switch</c> branch
    /// is covered directly, plus the three historical regressions that motivated the extraction:
    /// <list type="bullet">
    ///   <item>BUG-1 — Eat/Drink must reduce only their own need, never double-counted.</item>
    ///   <item>BUG-2 — Sleep must not deplete energy (recovery is a one-time Handle effect).</item>
    ///   <item>BUG-3 — Sleep must still raise hunger/thirst (slowed metabolism), not freeze them.</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class PhysiologyDriftTests
    {
        private static readonly PhysiologyConfig Cfg = new();

        private const double Tol = 1e-9;

        // ── Awake / default branch ───────────────────────────────────────────────

        [TestMethod]
        public void ComputeDrift_Awake_UsesAwakeRatesForEveryChannel()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(action: null, h: 1.0, Cfg, hydrationGain: null, immuneDecayFactor: 1.0);

            Assert.AreEqual(-2.0, d.Energy, Tol);
            Assert.AreEqual(6.0, d.Hunger, Tol);
            Assert.AreEqual(8.0, d.Thirst, Tol);
            Assert.AreEqual(-Cfg.PainPassiveRecoveryPerHour, d.Pain, Tol);
            Assert.AreEqual(-0.3, d.Immune, Tol);
        }

        [TestMethod]
        public void ComputeDrift_UnknownAction_FallsBackToAwakeBranch()
        {
            var awake = DefaultPhysiologyEngine.ComputeDrift(null, 1.0, Cfg, null, 1.0);
            var work = DefaultPhysiologyEngine.ComputeDrift("Work", 1.0, Cfg, null, 1.0);

            Assert.AreEqual(awake, work, "Any non-special action must use the default awake rates.");
        }

        // ── Eat / Drink (BUG-1: no double-counting) ──────────────────────────────

        [TestMethod]
        public void ComputeDrift_Eat_ReducesHungerOnly_LeavesThirstOnAwakeRate()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(Eat, 1.0, Cfg, null, 1.0);

            Assert.AreEqual(-40.0, d.Hunger, Tol, "Eating must reduce hunger.");
            Assert.AreEqual(8.0, d.Thirst, Tol, "BUG-1: eating must NOT also consume thirst.");
            Assert.AreEqual(-2.0, d.Energy, Tol, "Eating is an awake action — energy still depletes.");
        }

        [TestMethod]
        public void ComputeDrift_Drink_ReducesThirstOnly_LeavesHungerOnAwakeRate()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(Drink, 1.0, Cfg, null, 1.0);

            Assert.AreEqual(-50.0, d.Thirst, Tol, "Drinking must reduce thirst by the config default.");
            Assert.AreEqual(6.0, d.Hunger, Tol, "BUG-1: drinking must NOT also consume hunger.");
        }

        [TestMethod]
        public void ComputeDrift_Drink_UsesPerObjectHydrationGainWhenSupplied()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(Drink, 1.0, Cfg, hydrationGain: 30.0, immuneDecayFactor: 1.0);

            Assert.AreEqual(-30.0, d.Thirst, Tol, "Drink must honour the per-object hydration value.");
        }

        // ── Sleep (BUG-2: energy; BUG-3: hunger/thirst) ──────────────────────────

        [TestMethod]
        public void ComputeDrift_Sleep_DoesNotDepleteEnergy()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(Sleep, 5.0, Cfg, null, 1.0);

            Assert.AreEqual(0.0, d.Energy, Tol,
                "BUG-2: sleep must not deplete energy here — recovery is a one-time Handle(SleepEnded) effect.");
        }

        [TestMethod]
        public void ComputeDrift_Sleep_StillRaisesHungerAndThirstAtSlowedRate()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(Sleep, 1.0, Cfg, null, 1.0);

            Assert.AreEqual(2.0, d.Hunger, Tol, "BUG-3: hunger must still rise during sleep (slowed metabolism).");
            Assert.AreEqual(2.0, d.Thirst, Tol, "BUG-3: thirst must still rise during sleep.");
            Assert.IsTrue(d.Hunger < 6.0 && d.Thirst < 8.0, "Sleep rates must be slower than awake rates.");
        }

        [TestMethod]
        public void ComputeDrift_Sleep_AddsPassivePlusSleepPainRecovery()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(Sleep, 1.0, Cfg, null, 1.0);

            Assert.AreEqual(-(Cfg.PainPassiveRecoveryPerHour + Cfg.PainSleepRecoveryPerHour), d.Pain, Tol);
        }

        // ── SelfCare ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void ComputeDrift_SelfCare_UsesSelfCareRates()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(SelfCare, 1.0, Cfg, null, 1.0);

            Assert.AreEqual(-0.5, d.Energy, Tol, "Self-care depletes energy slower than the awake rate.");
            Assert.AreEqual(-10.0, d.Pain, Tol, "Self-care is the strongest pain recovery.");
            Assert.AreEqual(-0.5, d.Immune, Tol, "Self-care recovers immune load faster.");
            Assert.AreEqual(6.0, d.Hunger, Tol, "Self-care does not change hunger from the awake rate.");
        }

        // ── Time scaling ─────────────────────────────────────────────────────────

        [TestMethod]
        public void ComputeDrift_ScalesLinearlyWithElapsedHours()
        {
            var one = DefaultPhysiologyEngine.ComputeDrift(null, 1.0, Cfg, null, 1.0);
            var two = DefaultPhysiologyEngine.ComputeDrift(null, 2.0, Cfg, null, 1.0);

            Assert.AreEqual(one.Energy * 2.0, two.Energy, Tol);
            Assert.AreEqual(one.Hunger * 2.0, two.Hunger, Tol);
            Assert.AreEqual(one.Thirst * 2.0, two.Thirst, Tol);
            Assert.AreEqual(one.Pain * 2.0, two.Pain, Tol);
            Assert.AreEqual(one.Immune * 2.0, two.Immune, Tol);
        }

        [TestMethod]
        public void ComputeDrift_ZeroHours_ProducesZeroDrift()
        {
            var d = DefaultPhysiologyEngine.ComputeDrift(SelfCare, 0.0, Cfg, null, 1.0);

            Assert.AreEqual(0.0, d.Energy, Tol);
            Assert.AreEqual(0.0, d.Hunger, Tol);
            Assert.AreEqual(0.0, d.Thirst, Tol);
            Assert.AreEqual(0.0, d.Pain, Tol);
            Assert.AreEqual(0.0, d.Immune, Tol);
        }

        // ── Immune decay factor ──────────────────────────────────────────────────

        [TestMethod]
        public void ComputeDrift_ImmuneDecayFactor_ScalesImmuneRecovery()
        {
            var awake = DefaultPhysiologyEngine.ComputeDrift(null, 1.0, Cfg, null, immuneDecayFactor: 0.7);
            var care = DefaultPhysiologyEngine.ComputeDrift(SelfCare, 1.0, Cfg, null, immuneDecayFactor: 0.7);

            Assert.AreEqual(-0.3 * 0.7, awake.Immune, Tol, "Post-menopause factor must slow awake immune recovery.");
            Assert.AreEqual(-0.5 * 0.7, care.Immune, Tol, "Post-menopause factor must slow self-care immune recovery.");
        }

        [TestMethod]
        public void ComputeDrift_ImmuneDecayFactor_DoesNotAffectOtherChannels()
        {
            var full = DefaultPhysiologyEngine.ComputeDrift(null, 1.0, Cfg, null, immuneDecayFactor: 1.0);
            var slowed = DefaultPhysiologyEngine.ComputeDrift(null, 1.0, Cfg, null, immuneDecayFactor: 0.7);

            Assert.AreEqual(full.Energy, slowed.Energy, Tol);
            Assert.AreEqual(full.Hunger, slowed.Hunger, Tol);
            Assert.AreEqual(full.Thirst, slowed.Thirst, Tol);
            Assert.AreEqual(full.Pain, slowed.Pain, Tol);
            Assert.AreNotEqual(full.Immune, slowed.Immune, "Only the immune channel may change with the decay factor.");
        }
    }
}
