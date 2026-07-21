// DialogueInterpretationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Psychology.Appraisal;
    using GameEngineTools.Dialogue.Interpretation;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

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
                with { Directness = directness, Polarity = Polarity.Affirmative, ForceShift = forceShift };

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

        #endregion

        #region Appraiser — feeds CPM only on divergence

        private static PsychologyState Neutral => new(0.0, 0.3, 0.5, 20.0, 10.0, DiscreteEmotion.Neutral);

        private static PerceivedMeaning Perceived(SpeechAct source, Directness perceivedDirectness, Polarity perceivedPolarity)
            => new()
            {
                Source = source,
                PerceivedPoint = source.Point,
                PerceivedPolarity = perceivedPolarity,
                PerceivedDirectness = perceivedDirectness,
                ResolvedRoles = ImmutableDictionary<FgdFunctor, EntityRef>.Empty,
                Confidence = 0.8,
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

        #endregion
    }
}
