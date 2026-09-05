// DialogueInterpretationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Psychology.Appraisal;
    using GameEngineTools.Dialogue.Interpretation;
    using GameEngineTools.Dialogue.Semantics;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Immutable;
    using System.Linq;

    /// <summary>
    /// Phase-4 listener side: the same objective <see cref="SpeechAct"/> is read differently depending
    /// on the listener (hostility → directness shift, ToM/familiarity → irony), deterministically, with
    /// the source act preserved — and the divergence feeds the CPM via <see cref="PerceivedActAppraiser"/>.
    /// </summary>
    [TestClass]
    public class DialogueInterpretationTests
    {
        private static readonly HumanId Speaker = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        private static readonly HumanId Addressee = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        private static SpeechAct Act(Directness directness = Directness.Neutral, ForceShift? forceShift = null)
            => SpeechAct.Relational(RelationalActKind.SmallTalk, Speaker, Addressee, new WDateTime(1000))
                with
            { Directness = directness, Polarity = Polarity.Affirmative, ForceShift = forceShift };

        private static readonly ForceShift IronicShift = new(IllocutionaryPoint.Expressive, Polarity.Negative);

        #region Interpreter — divergence

        [TestMethod]
        public void Appraise_TrustingListener_PreservesDirectness()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var pm = interpreter.Appraise(Act(Directness.Neutral), new ListenerContext(2, 60, Hostility: 0.0));
            Assert.AreEqual(Directness.Neutral, pm.PerceivedDirectness);
        }

        [TestMethod]
        public void Appraise_HostileListener_ShiftsDirectnessTowardBlunt()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var pm = interpreter.Appraise(Act(Directness.Neutral), new ListenerContext(2, 60, Hostility: 0.8));
            Assert.AreEqual(Directness.Blunt, pm.PerceivedDirectness);
        }

        // Continuous "added negativity" (van den Berg & Lansu 2020): with gain 1.0 and an Indirect
        // act, the perceived rank crosses two transition points across these samples — a graded,
        // monotonically non-decreasing shift, not a binary jump at one configured threshold.
        [DataTestMethod]
        [DataRow(0.0, Directness.Indirect)]
        [DataRow(0.3, Directness.Neutral)]
        [DataRow(0.55, Directness.Neutral)]
        [DataRow(0.9, Directness.Blunt)]
        public void Appraise_HostilityShift_IsContinuousAndMonotonic(double hostility, Directness expected)
        {
            var interpreter = new DefaultSpeechActInterpreter(new SpeechActInterpreterConfig(HostilityGain: 1.0));
            var pm = interpreter.Appraise(Act(Directness.Indirect), new ListenerContext(2, 60, hostility));
            Assert.AreEqual(expected, pm.PerceivedDirectness);
        }

        [TestMethod]
        public void Appraise_SameActTwoListeners_Diverges()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var act = Act(Directness.Neutral);

            var trusting = interpreter.Appraise(act, new ListenerContext(2, 60, 0.0));
            var hostile = interpreter.Appraise(act, new ListenerContext(2, 60, 0.9));

            Assert.AreNotEqual(trusting.PerceivedDirectness, hostile.PerceivedDirectness);
        }

        [TestMethod]
        public void Appraise_LowTomListener_ReadsIronyLiterally()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var pm = interpreter.Appraise(Act(forceShift: IronicShift), new ListenerContext(TheoryOfMindLevel: 1, 80, 0.0));
            Assert.AreEqual(Polarity.Negative, pm.PerceivedPolarity);   // surface, taken literally
        }

        [TestMethod]
        public void Appraise_HighTomFamiliarListener_DecodesIrony()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var pm = interpreter.Appraise(Act(forceShift: IronicShift), new ListenerContext(TheoryOfMindLevel: 2, 80, 0.0));
            Assert.AreEqual(Polarity.Affirmative, pm.PerceivedPolarity); // intended, decoded
        }

        [TestMethod]
        public void Appraise_UnfamiliarListener_HasLowerConfidence()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var stranger = interpreter.Appraise(Act(), new ListenerContext(2, 0, 0.0));
            var intimate = interpreter.Appraise(Act(), new ListenerContext(2, 90, 0.0));
            Assert.IsTrue(stranger.Confidence < intimate.Confidence);
        }

        [TestMethod]
        public void Appraise_PreservesSourceActUnchanged()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var act = Act(Directness.Indirect);
            var pm = interpreter.Appraise(act, new ListenerContext(1, 10, 0.9));
            Assert.AreSame(act, pm.Source);
            Assert.AreEqual(Directness.Indirect, pm.Source.Directness); // original never mutated
        }

        #region Connotation layer (opt-in)

        private static DefaultSpeechActInterpreter ConnotationInterpreter(bool enabled)
            => new(new SpeechActInterpreterConfig(EnableConnotationLayer: enabled), new CuratedConnotationLexicon());

        [TestMethod]
        public void Appraise_ConnotationDisabled_IsIdenticalToBaseline()
        {
            // Regression guard: with the flag off, a wired lexicon must change NOTHING — zero tolerance.
            var baseline = new DefaultSpeechActInterpreter();
            var withLexicon = ConnotationInterpreter(enabled: false);
            var acts = new[]
            {
                Act(Directness.Neutral) with { PredicateLemma = "chválit" },
                Act(Directness.Indirect, IronicShift) with { PredicateLemma = "to se povedlo" },
            };
            var listeners = new[] { new ListenerContext(1, 10, 0.9), new ListenerContext(4, 80, 0.0) };

            foreach (var act in acts)
            {
                foreach (var listener in listeners)
                {
                    var a = baseline.Appraise(act, listener);
                    var b = withLexicon.Appraise(act, listener);
                    Assert.AreEqual(a.PerceivedPoint, b.PerceivedPoint);
                    Assert.AreEqual(a.PerceivedPolarity, b.PerceivedPolarity);
                    Assert.AreEqual(a.PerceivedDirectness, b.PerceivedDirectness);
                    Assert.AreEqual(a.Confidence, b.Confidence);
                    Assert.AreEqual(0.0, a.ConnotationDelta);
                    Assert.AreEqual(0.0, b.ConnotationDelta);
                }
            }
        }

        [TestMethod]
        public void Appraise_ConnotationEnabled_SameStructureDifferentLemma_DiffersInDelta()
        {
            // Phase-0 pair #1: praise vs assent — identical Point/Directness/Register, different warmth.
            var interpreter = ConnotationInterpreter(enabled: true);
            var listener = new ListenerContext(4, 60, 0.0);

            var praise = interpreter.Appraise(Act(Directness.Neutral) with { PredicateLemma = "chválit" }, listener);
            var assent = interpreter.Appraise(Act(Directness.Neutral) with { PredicateLemma = "souhlasit" }, listener);

            Assert.IsTrue(praise.ConnotationDelta > assent.ConnotationDelta);
            Assert.IsTrue(assent.ConnotationDelta > 0.0);
        }

        [TestMethod]
        public void Appraise_ConventionalIronicPhrase_DecodedEvenByLowTomListener()
        {
            // Graded Salience bypass: "to se povedlo" (Conventionality 0.9) decodes below the ToM gate;
            // a novel ironic act with the same listener stays literal.
            var interpreter = ConnotationInterpreter(enabled: true);
            var lowTom = new ListenerContext(TheoryOfMindLevel: 1, 10, 0.0);

            var conventional = Act(forceShift: IronicShift) with { PredicateLemma = "to se povedlo" };
            var novel = Act(forceShift: IronicShift) with { PredicateLemma = "chválit" };

            Assert.AreEqual(Polarity.Affirmative, interpreter.Appraise(conventional, lowTom).PerceivedPolarity);
            Assert.AreEqual(Polarity.Negative, interpreter.Appraise(novel, lowTom).PerceivedPolarity);
        }

        [TestMethod]
        public void Appraise_PowerAgency_ZeroWhenLayerOff_NonZeroWhenOn()
        {
            var act = Act(Directness.Neutral) with { PredicateLemma = "vyžadovat" };
            var listener = new ListenerContext(4, 60, 0.0);

            Assert.AreEqual(0.0, ConnotationInterpreter(enabled: false).Appraise(act, listener).PerceivedPowerDelta);
            Assert.AreEqual(0.0, ConnotationInterpreter(enabled: false).Appraise(act, listener).PerceivedAgencyDelta);

            var on = ConnotationInterpreter(enabled: true).Appraise(act, listener);
            Assert.IsTrue(on.PerceivedPowerDelta > 0.0, "A demand claims power over the addressee.");
            Assert.IsTrue(on.PerceivedAgencyDelta > 0.0, "A demand is high-agency.");
        }

        [DataTestMethod]
        [DataRow("vyžadovat", "požádat")]   // demand dominates a deferential request
        [DataRow("vyžadovat", "žebrat o")]  // demand dominates begging
        [DataRow("požádat", "žebrat o")]    // a request still outranks begging
        public void Appraise_PowerFrame_OrdersDirectiveVerbsBySocialPower(string dominant, string subordinate)
        {
            var listener = new ListenerContext(4, 60, 0.0);
            var interpreter = ConnotationInterpreter(enabled: true);

            var hi = interpreter.Appraise(Act(Directness.Neutral) with { PredicateLemma = dominant }, listener);
            var lo = interpreter.Appraise(Act(Directness.Neutral) with { PredicateLemma = subordinate }, listener);

            Assert.IsTrue(hi.PerceivedPowerDelta > lo.PerceivedPowerDelta,
                $"'{dominant}' must read as more powerful than '{subordinate}'.");
        }

        [TestMethod]
        public void Appraise_PowerAgency_DoNotLeakIntoEmotionalAppraisal()
        {
            // "vyžadovat" carries a strong power signal (0.8 × 0.15 = 0.12) and a weak negative valence
            // (−0.30 × 0.15 = −0.045). The CPM appraisal must be driven by valence ALONE — its pleasantness
            // equals the connotation valence, with power/agency contributing nothing.
            var listener = new ListenerContext(4, 60, 0.0);
            var pm = ConnotationInterpreter(enabled: true).Appraise(
                Act(Directness.Neutral) with { PredicateLemma = "vyžadovat" }, listener);

            Assert.IsTrue(pm.PerceivedPowerDelta > 0.1, "sanity: the strong power signal is present");
            var outcome = PerceivedActAppraiser.ToAppraisal(pm, 60, Neutral);
            Assert.IsNotNull(outcome);
            Assert.AreEqual(pm.ConnotationDelta, outcome!.IntrinsicPleasantness, 1e-9,
                "the appraisal must equal the connotation valence — power/agency did not enter the CPM.");
        }

        [TestMethod]
        public void AmbientInterpretation_ConfigureEnablesConnotation_ResetRestoresDefault()
        {
            // The engine paths (Psychology, Memory) read SpeechActInterpretation.Current — this is the
            // switch WorldObserver flips for the live experiment.
            try
            {
                SpeechActInterpretation.Configure(
                    new SpeechActInterpreterConfig(EnableConnotationLayer: true), new CuratedConnotationLexicon());
                var on = SpeechActInterpretation.Current.Appraise(
                    Act(Directness.Neutral) with { PredicateLemma = "chválit" }, new ListenerContext(4, 60, 0.0));
                Assert.IsTrue(on.ConnotationDelta > 0.0);
            }
            finally
            {
                SpeechActInterpretation.Reset();
            }

            var off = SpeechActInterpretation.Current.Appraise(
                Act(Directness.Neutral) with { PredicateLemma = "chválit" }, new ListenerContext(4, 60, 0.0));
            Assert.AreEqual(0.0, off.ConnotationDelta);
        }

        [TestMethod]
        public void Appraise_ConnotationDelta_IsClampedBySmallWeight()
        {
            var interpreter = ConnotationInterpreter(enabled: true);
            var pm = interpreter.Appraise(Act(Directness.Neutral) with { PredicateLemma = "oceňovat" }, new ListenerContext(4, 60, 0.0));

            // 0.70 valence × 0.15 weight = 0.105 — small, additive, well inside the ±0.3 clamp.
            Assert.AreEqual(0.105, pm.ConnotationDelta, 1e-9);
        }

        #endregion Connotation layer (opt-in)

        [TestMethod]
        public void Appraise_IsDeterministic()
        {
            var interpreter = new DefaultSpeechActInterpreter();
            var act = Act(Directness.Neutral, IronicShift);
            var ctx = new ListenerContext(2, 55, 0.7);

            var a = interpreter.Appraise(act, ctx);
            var b = interpreter.Appraise(act, ctx);

            Assert.AreEqual(a.PerceivedPoint, b.PerceivedPoint);
            Assert.AreEqual(a.PerceivedPolarity, b.PerceivedPolarity);
            Assert.AreEqual(a.PerceivedDirectness, b.PerceivedDirectness);
            Assert.AreEqual(a.Confidence, b.Confidence);
            CollectionAssert.AreEquivalent(a.ResolvedRoles.ToList(), b.ResolvedRoles.ToList());
        }

        #endregion Interpreter — divergence

        #region Appraiser — feeds CPM only on divergence

        private static PsychologyState Neutral => new(0.0, 0.3, 0.5, 20.0, 10.0, DiscreteEmotion.Neutral);

        private static PerceivedMeaning Perceived(
            SpeechAct source, Directness perceivedDirectness, Polarity perceivedPolarity, double connotationDelta = 0.0)
            => new()
            {
                Source = source,
                PerceivedPoint = source.Point,
                PerceivedPolarity = perceivedPolarity,
                PerceivedDirectness = perceivedDirectness,
                ResolvedRoles = ImmutableDictionary<FgdFunctor, EntityRef>.Empty,
                Confidence = 0.8,
                ConnotationDelta = connotationDelta,
            };

        [TestMethod]
        public void ToAppraisal_PlainReading_ReturnsNull()
        {
            var act = Act(Directness.Neutral);
            var pm = Perceived(act, Directness.Neutral, Polarity.Affirmative);
            Assert.IsNull(PerceivedActAppraiser.ToAppraisal(pm, familiarity: 50, Neutral));
        }

        [TestMethod]
        public void ToAppraisal_HostileShift_IsNegativeAndRelevant()
        {
            var act = Act(Directness.Neutral);
            var pm = Perceived(act, Directness.Blunt, Polarity.Affirmative); // felt harsher than sent

            var outcome = PerceivedActAppraiser.ToAppraisal(pm, familiarity: 60, Neutral);

            Assert.IsNotNull(outcome);
            Assert.IsTrue(outcome!.IsRelevant());
            Assert.IsTrue(outcome.IntrinsicPleasantness < 0);
            Assert.AreEqual(AppraisalAgency.Other, outcome.Agency);
        }

        [TestMethod]
        public void ToAppraisal_DecodedIrony_IsPositive()
        {
            var act = Act(Directness.Neutral, IronicShift); // intended Affirmative, surface Negative
            var pm = Perceived(act, Directness.Neutral, Polarity.Affirmative); // decoded to intended

            var outcome = PerceivedActAppraiser.ToAppraisal(pm, familiarity: 70, Neutral);

            Assert.IsNotNull(outcome);
            Assert.IsTrue(outcome!.IntrinsicPleasantness > 0);
        }

        [TestMethod]
        public void ToAppraisal_ConnotationAlone_MakesPlainlyReadActRelevant()
        {
            // No divergence at all — the warm word itself carries the affect ("chválit": 0.6 × 0.15).
            var act = Act(Directness.Neutral);
            var pm = Perceived(act, Directness.Neutral, Polarity.Affirmative, connotationDelta: 0.09);

            var outcome = PerceivedActAppraiser.ToAppraisal(pm, familiarity: 60, Neutral);

            Assert.IsNotNull(outcome);
            Assert.IsTrue(outcome!.IntrinsicPleasantness > 0);
        }

        [TestMethod]
        public void ToAppraisal_NegativeConnotation_StacksWithHostileShift()
        {
            var act = Act(Directness.Neutral);
            var shiftOnly = Perceived(act, Directness.Blunt, Polarity.Affirmative);
            var shiftPlusSting = Perceived(act, Directness.Blunt, Polarity.Affirmative, connotationDelta: -0.075);

            var a = PerceivedActAppraiser.ToAppraisal(shiftOnly, familiarity: 60, Neutral)!;
            var b = PerceivedActAppraiser.ToAppraisal(shiftPlusSting, familiarity: 60, Neutral)!;

            Assert.IsTrue(b.IntrinsicPleasantness < a.IntrinsicPleasantness, "The stinging word must deepen the hostile reading.");
        }

        [TestMethod]
        public void EndToEnd_PraiseVsAssent_FlagOn_DivergesInEmotionalAppraisal()
        {
            // The measurable-benefit experiment surface for the Phase-2 gate: same structure, different
            // lemma → different CPM appraisal. Flag off (default engine paths) stays inert.
            var interpreter = ConnotationInterpreter(enabled: true);
            var listener = new ListenerContext(4, 60, 0.0);

            var praise = interpreter.Appraise(Act(Directness.Neutral) with { PredicateLemma = "chválit" }, listener);
            var assent = interpreter.Appraise(Act(Directness.Neutral) with { PredicateLemma = "souhlasit" }, listener);

            var praiseOutcome = PerceivedActAppraiser.ToAppraisal(praise, familiarity: 60, Neutral);
            var assentOutcome = PerceivedActAppraiser.ToAppraisal(assent, familiarity: 60, Neutral);

            Assert.IsNotNull(praiseOutcome);
            Assert.IsNotNull(assentOutcome);
            Assert.IsTrue(praiseOutcome!.IntrinsicPleasantness > assentOutcome!.IntrinsicPleasantness);
        }

        #endregion Appraiser — feeds CPM only on divergence
    }
}
