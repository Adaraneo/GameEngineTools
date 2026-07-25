// DialogueConnotationDecisionTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Psychology.Appraisal;
    using GameEngineTools.Dialogue.Interpretation;
    using GameEngineTools.Dialogue.Semantics;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// <b>Decision tests</b> — automated, deterministic answers to "is the next step worth it?", the
    /// same role the Fáze-0 decision test played for Fáze 1. Each test measures a real effect size and
    /// asserts it lands in a "worth proceeding" range; a failure is the signal to recalibrate or stop.
    /// Measured values (this calibration) are recorded inline so the numbers are visible without a run.
    /// </summary>
    [TestClass]
    public class DialogueConnotationDecisionTests
    {
        private static readonly HumanId Sp = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        private static readonly HumanId Ad = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        private static SpeechAct Act(string lemma)
            => SpeechAct.Relational(RelationalActKind.Validation, Sp, Ad, new WDateTime(1000)) with { PredicateLemma = lemma };

        private static DefaultSpeechActInterpreter Interpreter(bool flagOn)
            => new(new SpeechActInterpreterConfig(EnableConnotationLayer: flagOn), new CuratedConnotationLexicon());

        /// <summary>Cumulative valence pull a repeated act would exert on emotion over <paramref name="n"/> acts.</summary>
        private static double CumulativeValencePull(string lemma, bool flagOn, int n)
        {
            var interpreter = Interpreter(flagOn);
            var config = new PsychologyConfig();
            var current = new PsychologyState(0, 0.3, 0.5, 20, 10, DiscreteEmotion.Neutral);
            var listener = new ListenerContext(4, 60, 0.0);

            var sum = 0.0;
            for (var i = 0; i < n; i++)
            {
                var pm = interpreter.Appraise(Act(lemma), listener);
                var outcome = PerceivedActAppraiser.ToAppraisal(pm, 60, current);
                if (outcome is { } o && o.IsRelevant())
                {
                    sum += AppraisalEmotionMap.Map(o, config).DeltaValence;
                }
            }

            return sum;
        }

        private static double PowerDelta(string lemma)
            => Interpreter(flagOn: true).Appraise(Act(lemma), new ListenerContext(4, 60, 0.0)).PerceivedPowerDelta;

        // ── DECISION 1 — does the connotation layer move emotion enough to be worth carrying? ──
        // Measured (20 acts): warm(chválit)=2.59, flag-off=0.00. YES — a strong, real signal.
        [TestMethod]
        public void Decision_ConnotationLayer_MovesEmotionMeaningfully()
        {
            var warmOn = CumulativeValencePull("chválit", flagOn: true, n: 20);
            var warmOff = CumulativeValencePull("chválit", flagOn: false, n: 20);

            Assert.AreEqual(0.0, warmOff, "control: with the flag off the layer must contribute nothing");
            Assert.IsTrue(warmOn >= 1.0,
                $"connotation must move emotion meaningfully to justify Phase 2 — measured {warmOn:F2} over 20 acts");
        }

        // ── DECISION 2 — CHARACTERIZATION: the current calibration is a CLIFF, not graded. ──
        // A warm-but-below-threshold verb (souhlasit, 0.06 valence → relevance 0.048 < 0.05) is inert,
        // while a stronger one (chválit, 0.09) fires fully. This is the finding that decides whether the
        // relevance gate needs smoothing before word choice reads as *graded* rather than on/off.
        // If this test starts FAILING, the calibration changed — revisit whether graded connotation is wanted.
        [TestMethod]
        public void Decision_ConnotationCalibration_IsAThresholdCliff_NotGraded()
        {
            var strong = CumulativeValencePull("chválit", flagOn: true, n: 20);   // 0.09 valence → fires
            var nearThreshold = CumulativeValencePull("souhlasit", flagOn: true, n: 20); // 0.06 → inert today

            Assert.IsTrue(strong > 1.0, "the strong warm verb fires");
            Assert.AreEqual(0.0, nearThreshold,
                "near-threshold warm verb is currently INERT — cliff. Smooth the relevance gate for graded connotation.");
        }

        // ── DECISION 3 — does the power frame give Phase 2b a clean, separable signal? ──
        // Measured: vyžadovat=+0.12, požádat=-0.03, žebrat=-0.12 → spread 0.24. YES — cleanly ordered.
        [TestMethod]
        public void Decision_PowerFrame_GivesPhase2bASeparableSignal()
        {
            var demand = PowerDelta("vyžadovat");
            var request = PowerDelta("požádat");
            var beg = PowerDelta("žebrat o");

            Assert.IsTrue(demand > request && request > beg, "power must order demand > request > beg");
            Assert.IsTrue(demand - beg >= 0.2,
                $"Phase 2b needs a clear power spread to propagate — measured {demand - beg:F2}");
        }

        // ── DECISION 4 — Phase-2b SPIKE: if we fed perceived power into Respect, would it visibly diverge? ──
        // Simulates (does NOT wire) a candidate propagation Respect += power × gain. Over 20 acts a
        // demander and a beggar diverge in Respect by a clearly-visible margin — so the invasive Phase-2b
        // wiring would have a real, observable effect. The gain here is a placeholder to be calibrated in 2b.
        [TestMethod]
        public void Decision_Phase2bSpike_PowerIntoRespect_ProducesVisibleDivergence()
        {
            const double CandidateRespectGain = 20.0;   // Respect is 0..100; per-act bump = power(±0.12)×gain
            const int Acts = 20;

            var respectFromDemand = 0.0;
            var respectFromBeg = 0.0;
            for (var i = 0; i < Acts; i++)
            {
                respectFromDemand += PowerDelta("vyžadovat") * CandidateRespectGain;
                respectFromBeg += PowerDelta("žebrat o") * CandidateRespectGain;
            }

            var divergence = respectFromDemand - respectFromBeg;   // ≈ 0.24 × 20 × 20 ≈ 96 (before clamping)
            Assert.IsTrue(divergence >= 20.0,
                $"a demander vs a beggar would separate in Respect by {divergence:F0} pts over {Acts} acts — Phase 2b is worth wiring");
        }
    }
}
