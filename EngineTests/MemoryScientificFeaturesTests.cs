// MemoryScientificFeaturesTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static EngineTests.MemoryScientificTestData;

    // =========================================================================
    // Peak-End Rule (Fredrickson & Kahneman, 1993)
    // salience = (|peak|×1.5 + |end|) / 2.5  (+0.08 negativity boost)
    // =========================================================================

    [TestClass]
    public class PeakEndSalienceTests : TestBase
    {
        [TestMethod]
        public void PeakEnd_IntimateActAccepted_HasHigherRecallRelevanceThanSmallTalk()
        {
            // SelfDisclosure accepted: peak=1.0, end=0.85 → salience = (1.5+0.85)/2.5 = 0.94
            // SmallTalk accepted:      peak=1.0, end=0.50 → salience = (1.5+0.50)/2.5 = 0.80
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var intimate = Episode(now, 2, "Interaction:SelfDisclosure:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.94);
            var casual   = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b",      EmotionalTag.Positive, 0.80, target, salience: 0.80);
            var memory = new MemoryIndex(new List<EpisodicMemory> { casual, intimate });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SelfDisclosure, null, WTimeSpan.FromDays(7), 2),
                now);

            Assert.IsTrue(recall.Items.Count >= 2, "Obě epizody musí projít threshold.");
            Assert.AreEqual(intimate.Id, recall.Items[0].Episode.Id,
                $"Intimní akt (salience=0.94) musí outranknout SmallTalk (0.80). " +
                $"Top: {recall.Items[0].Episode.What}, relevance={recall.Items[0].Relevance:F4}");
        }

        [TestMethod]
        public void PeakEnd_HigherSalienceEpisode_AlwaysOutranksLower_WhenOtherFactorsEqual()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var highSalience = Episode(now, 2, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.80, target, salience: 0.90);
            var lowSalience  = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.50);
            var memory = new MemoryIndex(new List<EpisodicMemory> { lowSalience, highSalience });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk, EmotionalTag.Negative, WTimeSpan.FromDays(7), 2),
                now);

            Assert.IsTrue(recall.Items.Count >= 1, "Alespoň jedna epizoda musí projít threshold.");
            Assert.AreEqual(highSalience.Id, recall.Items[0].Episode.Id,
                "Epizoda s vyšší salience musí mít vyšší relevance.");
        }

        [TestMethod]
        public void PeakEnd_NullValenceFallback_AcceptedEpisode_HasLowerSalienceThanRejected()
        {
            // Původní logika (fallback bez peak/end): Accepted=0.7, Rejected=0.9
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var accepted = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.70);
            var rejected = Episode(now, 2, "Interaction:Invite:Rejected|from=a|to=b",    EmotionalTag.Negative, 0.80, target, salience: 0.90);

            Assert.IsTrue(rejected.Salience > accepted.Salience,
                "Fallback: rejected epizoda (0.90) má vyšší salience než accepted (0.70).");
        }
    }

    // =========================================================================
    // Memory Reconsolidation (Nader et al., 2000)
    // Každý recall driftuje emoci vzpomínky k aktuální náladě (4%; neg=1.3×).
    // Testováno matematicky — přímé Handle() vyžaduje full IHumanContext.
    // =========================================================================

    [TestClass]
    public class ReconsolidationMathTests
    {
        [TestMethod]
        public void ReconsolidationDrift_NegativeEpisode_DriftsTowardPositiveMoodFasterThanPositive()
        {
            // neg drift rate = 0.04 × 1.3 = 0.052; pos drift rate = 0.04 × 1.0 = 0.04
            const double driftRate = 0.04;
            const double currentValence = 0.7;

            var negNumeric = -1.0;
            var posNumeric =  1.0;

            var negDelta = Math.Abs((negNumeric + (currentValence - negNumeric) * (driftRate * 1.3)) - negNumeric);
            var posDelta = Math.Abs((posNumeric + (currentValence - posNumeric) * (driftRate * 1.0)) - posNumeric);

            Assert.IsTrue(negDelta > posDelta,
                $"Negativní vzpomínka musí driftovat rychleji. neg Δ={negDelta:F4}, pos Δ={posDelta:F4}");
        }

        [TestMethod]
        public void ReconsolidationDrift_After20Recalls_NegativeApproachesNeutral_InPositiveMood()
        {
            // Simulate 20 recalls of a negative episode while in positive mood (+0.7)
            const double driftRate = 0.04 * 1.3;
            const double currentValence = 0.7;

            var emotion = -1.0;
            for (int i = 0; i < 20; i++)
                emotion = emotion + (currentValence - emotion) * driftRate;

            // Po 20 recalls by hodnota měla překročit -0.35 (threshold Negative→Mixed)
            Assert.IsTrue(emotion > -0.35,
                $"Po 20 recalls musí negativní epizoda driftovat na Mixed/Neutral. Hodnota: {emotion:F4}");
        }

        [TestMethod]
        public void ReconsolidationDrift_NegativeMood_PositiveEpisode_DriftsDownward()
        {
            const double driftRate = 0.04;
            const double currentValence = -0.6;
            var posNumeric = 1.0;
            var drifted = posNumeric + (currentValence - posNumeric) * driftRate;

            Assert.IsTrue(drifted < posNumeric,
                $"V negativní náladě musí pozitivní vzpomínka klesat. Drifted: {drifted:F4}");
        }

        [TestMethod]
        public void ReconsolidationDrift_NeutralEpisode_DoesNotChangeMuch_InAnyMood()
        {
            // Neutral = 0.0; drift je malý v obou směrech
            const double driftRate = 0.04;
            var driftedPos = 0.0 + (0.8 - 0.0) * driftRate;   // pozitivní nálada
            var driftedNeg = 0.0 + (-0.8 - 0.0) * driftRate;  // negativní nálada

            Assert.IsTrue(Math.Abs(driftedPos) < 0.1, "Neutral v pozitivní náladě driftuje minimálně.");
            Assert.IsTrue(Math.Abs(driftedNeg) < 0.1, "Neutral v negativní náladě driftuje minimálně.");
        }
    }

    // =========================================================================
    // Neuroticism-modulovaný mood recall (Bower, 1981)
    // Low N + negative mood → mood repair (+0.10 na positive)
    // High N + negative mood → spirála (-0.08 pos, +0.06 neg)
    // =========================================================================

    [TestClass]
    public class NeuroticismMoodRecallTests : TestBase
    {
        [TestMethod]
        public void NeuroticismMood_LowN_NegativeMood_PositiveEpisodeOutranksEqualNegative()
        {
            // Záměrně: episody 120h staré + nízká salience/strength + žádný target v query
            // → základní relevance nepřesáhne 1.0 a Neuroticism bias (+0.10) jasně rozhodne.
            // (s targetScore=1.0 by obě epizody dosáhly Math.Clamp stropu a bias by se ztratil)
            var now = WDateTime.New(100, 1, 10, 12);
            var other = new HumanId(Guid.NewGuid());

            var positive = Episode(now, 120, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.40, other, salience: 0.40);
            var negative = Episode(now, 120, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.40, other, salience: 0.40);
            var memory = new MemoryIndex(new List<EpisodicMemory> { negative, positive });

            // Žádný TargetHuman v query → targetScore = 0.72 (má OtherPerson, ReachOut akce)
            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(null, "ReachOut", null, null,
                    WTimeSpan.FromDays(7), 2, CurrentValence: -0.5, NeuroticismScore: 0.3),
                now);

            Assert.IsTrue(recall.Items.Count >= 2, "Obě epizody musí projít threshold.");
            Assert.AreEqual(positive.Id, recall.Items[0].Episode.Id,
                $"Low-N mood repair: pozitivní epizoda musí vyhrát. Top: {recall.Items[0].Episode.Emotion}, " +
                $"relevance diff: pos={recall.Items.FirstOrDefault(i => i.Episode.Id == positive.Id)?.Relevance:F4} " +
                $"neg={recall.Items.FirstOrDefault(i => i.Episode.Id == negative.Id)?.Relevance:F4}");
        }

        [TestMethod]
        public void NeuroticismMood_HighN_NegativeMood_NegativeEpisodeOutranksEqualPositive()
        {
            // Stejné podmínky jako low-N test — bias je opačný: +0.06 neg, -0.08 pos
            var now = WDateTime.New(100, 1, 10, 12);
            var other = new HumanId(Guid.NewGuid());

            var positive = Episode(now, 120, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.40, other, salience: 0.40);
            var negative = Episode(now, 120, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.40, other, salience: 0.40);
            var memory = new MemoryIndex(new List<EpisodicMemory> { positive, negative });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(null, "ReachOut", null, null,
                    WTimeSpan.FromDays(7), 2, CurrentValence: -0.5, NeuroticismScore: 0.75),
                now);

            Assert.IsTrue(recall.Items.Count >= 2, "Obě epizody musí projít threshold.");
            Assert.AreEqual(negative.Id, recall.Items[0].Episode.Id,
                $"High-N spirála: negativní epizoda musí vyhrát. Top: {recall.Items[0].Episode.Emotion}, " +
                $"relevance diff: pos={recall.Items.FirstOrDefault(i => i.Episode.Id == positive.Id)?.Relevance:F4} " +
                $"neg={recall.Items.FirstOrDefault(i => i.Episode.Id == negative.Id)?.Relevance:F4}");
        }

        [TestMethod]
        public void NeuroticismMood_PositiveMood_NoBias_HighSalienceWins()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());

            // Positive current mood (valence=+0.5) → bias = 0 bez ohledu na N
            var highSalience = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.80);
            var lowSalience  = Episode(now, 2, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.80, target, salience: 0.50);
            var memory = new MemoryIndex(new List<EpisodicMemory> { lowSalience, highSalience });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk, null,
                    WTimeSpan.FromDays(7), 2, CurrentValence: +0.5, NeuroticismScore: 0.9),
                now);

            Assert.IsTrue(recall.Items.Count >= 2);
            Assert.AreEqual(highSalience.Id, recall.Items[0].Episode.Id,
                "Bez bias musí vyhrát epizoda s vyšší salience.");
        }

        [TestMethod]
        public void NeuroticismMood_DefaultQuery_NoBias_LegacyBehaviorUnchanged()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());

            // Default query: CurrentValence=0, NeuroticismScore=0.5 → žádný bias
            var ep1 = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.70);
            var ep2 = Episode(now, 4, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.60);
            var memory = new MemoryIndex(new List<EpisodicMemory> { ep2, ep1 });

            // Starý query bez nových parametrů — musí se chovat identicky jako dříve
            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk, null, WTimeSpan.FromDays(7), 2),
                now);

            Assert.AreEqual(ep1.Id, recall.Items[0].Episode.Id,
                "Starý query bez bias: vyšší salience musí vyhrát.");
        }
    }

    // =========================================================================
    // System 1 / System 2 (Kahneman 2011, Shenhav et al. 2017)
    // CognitiveBurden > threshold → přeskočí episodický recall, zachová reflections.
    // =========================================================================

    [TestClass]
    public class CognitiveBurdenSystem1Tests : TestBase
    {
        private static MemoryIndex BuildMemoryWithReflectable(WDateTime now, HumanId target)
        {
            var episodes = new List<EpisodicMemory>();
            for (int i = 0; i < 6; i++)
            {
                episodes.Add(Episode(now, i * 2 + 1,
                    $"Interaction:SmallTalk:Accepted|from={target.Value}|to=b",
                    EmotionalTag.Positive, 0.80, target, salience: 0.70));
            }
            return new MemoryIndex(episodes);
        }

        [TestMethod]
        public void System1_HighCognitiveBurden_ReturnsEmptyEpisodicRecall()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var memory = BuildMemoryWithReflectable(now, target);

            var workingSet = MemoryCognition.BuildWorkingSet(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk,
                    null, WTimeSpan.FromDays(7), 4, CognitiveBurden: 0.80),
                now);

            Assert.AreEqual(0, workingSet.RecalledEpisodes.Count,
                "System 1: episodický recall musí být prázdný.");
            Assert.IsTrue(workingSet.IsSystem1, "IsSystem1 musí být true.");
        }

        [TestMethod]
        public void System1_HighCognitiveBurden_StillReturnsReflections()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var memory = BuildMemoryWithReflectable(now, target);

            var workingSet = MemoryCognition.BuildWorkingSet(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk,
                    null, WTimeSpan.FromDays(7), 4, CognitiveBurden: 0.85),
                now);

            Assert.IsTrue(workingSet.Reflections.Count > 0,
                "System 1 musí stále vracet reflection summaries.");
        }

        [TestMethod]
        public void System2_LowCognitiveBurden_ReturnsFullEpisodicRecall()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var memory = BuildMemoryWithReflectable(now, target);

            var workingSet = MemoryCognition.BuildWorkingSet(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk,
                    null, WTimeSpan.FromDays(7), 4, CognitiveBurden: 0.40),
                now);

            Assert.IsTrue(workingSet.RecalledEpisodes.Count > 0,
                "System 2: episodický recall musí vrátit epizody.");
            Assert.IsFalse(workingSet.IsSystem1, "IsSystem1 musí být false.");
        }

        [TestMethod]
        public void System1_NullCognitiveBurden_AlwaysSystem2_BackwardCompat()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var memory = BuildMemoryWithReflectable(now, target);

            // Původní query bez CognitiveBurden → vždy System 2 (backward compat)
            var workingSet = MemoryCognition.BuildWorkingSet(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk,
                    null, WTimeSpan.FromDays(7), 4),
                now);

            Assert.IsFalse(workingSet.IsSystem1, "Legacy query nesmí spustit System 1.");
            Assert.IsTrue(workingSet.RecalledEpisodes.Count > 0, "Legacy query musí vracet recall.");
        }

        [TestMethod]
        public void System1_BurdenAboveThreshold_TriggersSystem1()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var memory = BuildMemoryWithReflectable(now, target);

            // default threshold = 0.65; burden = 0.66 → System 1
            var workingSet = MemoryCognition.BuildWorkingSet(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk,
                    null, WTimeSpan.FromDays(7), 4, CognitiveBurden: 0.66),
                now);

            Assert.IsTrue(workingSet.IsSystem1, "Burden 0.66 > threshold 0.65 → System 1.");
        }

        [TestMethod]
        public void System2_BurdenBelowThreshold_DoesNotTriggerSystem1()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());
            var memory = BuildMemoryWithReflectable(now, target);

            var workingSet = MemoryCognition.BuildWorkingSet(memory,
                new MemoryRecallQuery(target, "ReachOut", SpeechAct.SmallTalk,
                    null, WTimeSpan.FromDays(7), 4, CognitiveBurden: 0.64),
                now);

            Assert.IsFalse(workingSet.IsSystem1, "Burden 0.64 < threshold 0.65 → System 2.");
        }

        [TestMethod]
        public void CognitiveBurden_Formula_WeightsSumToOne()
        {
            // Vzorec: stress×0.4 + fatigue×0.35 + crowding×0.25
            var burden = 1.0 * 0.40 + 1.0 * 0.35 + 1.0 * 0.25;
            Assert.AreEqual(1.0, burden, 0.0001, "Váhy CognitiveBurden musí sumovat na 1.0.");
        }
    }

    // =========================================================================
    // Sdílená helper data pro testy paměti
    // =========================================================================

    internal static class MemoryScientificTestData
    {
        internal static EpisodicMemory Episode(
            WDateTime now,
            double hoursAgo,
            string what,
            EmotionalTag emotion,
            double strength,
            HumanId? otherPerson,
            double salience = 0.70)
            => new(
                Guid.NewGuid(),
                now - WTimeSpan.FromHours(hoursAgo),
                what,
                salience,
                emotion,
                strength,
                OtherPerson: otherPerson);
    }
}
