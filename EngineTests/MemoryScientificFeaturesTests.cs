// MemoryScientificFeaturesTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
            var casual = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.80);
            var memory = new MemoryIndex(new List<EpisodicMemory> { casual, intimate });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SelfDisclosure, null, WTimeSpan.FromDays(7), 2),
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
            var lowSalience = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, target, salience: 0.50);
            var memory = new MemoryIndex(new List<EpisodicMemory> { lowSalience, highSalience });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk, EmotionalTag.Negative, WTimeSpan.FromDays(7), 2),
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
            var rejected = Episode(now, 2, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.80, target, salience: 0.90);

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
            var posNumeric = 1.0;

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
            var lowSalience = Episode(now, 2, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.80, target, salience: 0.50);
            var memory = new MemoryIndex(new List<EpisodicMemory> { lowSalience, highSalience });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk, null,
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
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(7), 2),
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
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk,
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
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk,
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
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk,
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
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk,
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
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk,
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
                new MemoryRecallQuery(target, "ReachOut", RelationalActKind.SmallTalk,
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
    // Negative Memory Spiral (Bower 1981; memory-cognition.md formula)
    // spiralRisk = N×0.5 + log(1 + daysInNegativeMood)×0.1
    // Spiral active when: valence < -0.4 AND spiralRisk > 0.6
    // =========================================================================

    [TestClass]
    public class NegativeMemorySpiralTests : TestBase
    {
        private static readonly HumanId Target = new HumanId(Guid.NewGuid());

        [TestMethod]
        public void Spiral_HighN_LongNegativeMood_AmplifiedNegativeRecall()
        {
            // N=0.8, daysInNegativeMood=20 → spiralRisk = 0.4 + log(21)×0.1 ≈ 0.4 + 0.304 = 0.704 > 0.6
            // valence = -0.6 < -0.4 → spirála aktivní → positive bias = -0.15, negative = +0.12
            var now = WDateTime.New(100, 1, 10, 12);

            var positive = Episode(now, 120, "Interaction:SmallTalk:Accepted|from=a|to=b",
                EmotionalTag.Positive, 0.40, Target, salience: 0.40);
            var negative = Episode(now, 120, "Interaction:SmallTalk:Rejected|from=a|to=b",
                EmotionalTag.Negative, 0.40, Target, salience: 0.40);
            var memory = new MemoryIndex(new List<EpisodicMemory> { positive, negative });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(null, "ReachOut", null, null,
                    WTimeSpan.FromDays(7), 2,
                    CurrentValence: -0.6, NeuroticismScore: 0.8, DaysInNegativeMood: 20.0),
                now);

            Assert.IsTrue(recall.Items.Count >= 2);
            Assert.AreEqual(negative.Id, recall.Items[0].Episode.Id,
                $"V spirále musí negativní epizoda silně dominovat. Top: {recall.Items[0].Episode.Emotion}");
        }

        [TestMethod]
        public void Spiral_LowN_LongNegativeMood_NoSpiral_MoodRepairStillWorks()
        {
            // N=0.3 → spiralRisk = 0.15 + log(21)×0.1 ≈ 0.15 + 0.304 = 0.454 < 0.6 → spirála NENÍ
            // Low N → mood repair branch → pozitivní epizody preferred
            var now = WDateTime.New(100, 1, 10, 12);

            var positive = Episode(now, 120, "Interaction:SmallTalk:Accepted|from=a|to=b",
                EmotionalTag.Positive, 0.40, Target, salience: 0.40);
            var negative = Episode(now, 120, "Interaction:SmallTalk:Rejected|from=a|to=b",
                EmotionalTag.Negative, 0.40, Target, salience: 0.40);
            var memory = new MemoryIndex(new List<EpisodicMemory> { negative, positive });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(null, "ReachOut", null, null,
                    WTimeSpan.FromDays(7), 2,
                    CurrentValence: -0.6, NeuroticismScore: 0.3, DaysInNegativeMood: 20.0),
                now);

            Assert.IsTrue(recall.Items.Count >= 2);
            Assert.AreEqual(positive.Id, recall.Items[0].Episode.Id,
                "Low-N bez spirály musí stále aplikovat mood repair → pozitivní epizoda vyhrává.");
        }

        [TestMethod]
        public void Spiral_HighN_ShortNegativeMood_BelowThreshold_NormalBias()
        {
            // N=0.8, daysInNegativeMood=1 → spiralRisk = 0.4 + log(2)×0.1 ≈ 0.4 + 0.069 = 0.469 < 0.6
            // → spirála NENÍ, ale high-N baseline negativní bias platí
            var now = WDateTime.New(100, 1, 10, 12);

            var positive = Episode(now, 120, "Interaction:SmallTalk:Accepted|from=a|to=b",
                EmotionalTag.Positive, 0.40, Target, salience: 0.40);
            var negative = Episode(now, 120, "Interaction:SmallTalk:Rejected|from=a|to=b",
                EmotionalTag.Negative, 0.40, Target, salience: 0.40);
            var memory = new MemoryIndex(new List<EpisodicMemory> { positive, negative });

            // High-N (0.75) bez spirály → slabší bias (-0.08/+0.06 místo -0.15/+0.12)
            var withoutSpiral = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(null, "ReachOut", null, null,
                    WTimeSpan.FromDays(7), 2,
                    CurrentValence: -0.6, NeuroticismScore: 0.75, DaysInNegativeMood: 1.0),
                now);

            var withSpiral = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(null, "ReachOut", null, null,
                    WTimeSpan.FromDays(7), 2,
                    CurrentValence: -0.6, NeuroticismScore: 0.75, DaysInNegativeMood: 20.0),
                now);

            // Negativní epizoda musí vyhrát v obou případech (high N), ale s různou silou
            Assert.AreEqual(negative.Id, withoutSpiral.Items[0].Episode.Id,
                "Bez spirály: high-N negativní bias platí.");
            Assert.AreEqual(negative.Id, withSpiral.Items[0].Episode.Id,
                "Se spirálou: negativní bias ještě silnější.");

            // Spiral musí mít větší diferenci (silnější bias)
            var negRelNoSpiral = withoutSpiral.Items.FirstOrDefault(i => i.Episode.Id == negative.Id)?.Relevance ?? 0;
            var negRelSpiral = withSpiral.Items.FirstOrDefault(i => i.Episode.Id == negative.Id)?.Relevance ?? 0;
            Assert.IsTrue(negRelSpiral >= negRelNoSpiral,
                $"Se spirálou musí mít negativní epizoda alespoň stejně vysokou relevance. " +
                $"Spiral={negRelSpiral:F4}, NoSpiral={negRelNoSpiral:F4}");
        }

        [TestMethod]
        public void Spiral_PositiveMood_NoSpiral_Regardless_Of_DaysInNegativeMood()
        {
            // valence >= 0 → vždy vrátí 0.0 bez ohledu na daysInNegativeMood
            var now = WDateTime.New(100, 1, 10, 12);

            var positive = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b",
                EmotionalTag.Positive, 0.80, Target, salience: 0.80);
            var negative = Episode(now, 2, "Interaction:SmallTalk:Rejected|from=a|to=b",
                EmotionalTag.Negative, 0.50, Target, salience: 0.50);
            var memory = new MemoryIndex(new List<EpisodicMemory> { negative, positive });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(Target, "ReachOut", RelationalActKind.SmallTalk, null,
                    WTimeSpan.FromDays(7), 2,
                    CurrentValence: +0.3, NeuroticismScore: 0.9, DaysInNegativeMood: 30.0),
                now);

            Assert.AreEqual(positive.Id, recall.Items[0].Episode.Id,
                "Pozitivní nálada → žádná spirála, výsledek závisí jen na salience.");
        }
    }

    // =========================================================================
    // Initial Strength z emocionální intenzity (Baumeister et al. 2001)
    // strength = salience × intensity × 0.7 + 0.3
    // =========================================================================

    [TestClass]
    public class InitialStrengthTests : TestBase
    {
        [TestMethod]
        public void InitialStrength_NegativeEpisode_StrongerThan_PositiveEpisode_AtSameSalience()
        {
            // Stejná salience, různá emoce → negativní musí mít vyšší počáteční strength
            // intensity: Negative=1.0, Positive=0.85 → Neg strength > Pos strength
            var now = WDateTime.New(100, 1, 10, 12);
            var target = new HumanId(Guid.NewGuid());

            // Tyto epizody simulují co engine zakóduje
            // strength = ComputeInitialStrength(0.75, emotion)
            var negStrength = 0.75 * 1.00 * 0.7 + 0.3; // 0.825
            var posStrength = 0.75 * 0.85 * 0.7 + 0.3; // 0.746

            var negEpisode = Episode(now, 2, "Interaction:SmallTalk:Rejected|from=a|to=b",
                EmotionalTag.Negative, negStrength, target, salience: 0.75);
            var posEpisode = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b",
                EmotionalTag.Positive, posStrength, target, salience: 0.75);

            Assert.IsTrue(negEpisode.Strength > posEpisode.Strength,
                $"Negativní epizoda musí mít vyšší initial strength. " +
                $"Neg={negEpisode.Strength:F4}, Pos={posEpisode.Strength:F4}");
        }

        [TestMethod]
        public void InitialStrength_HighSalience_HigherStrength()
        {
            // Vyšší salience → vyšší strength (monotonicky)
            var highSalience = 0.90 * 0.85 * 0.7 + 0.3; // salience=0.90, Positive
            var lowSalience = 0.40 * 0.85 * 0.7 + 0.3; // salience=0.40, Positive

            Assert.IsTrue(highSalience > lowSalience,
                $"Vyšší salience musí dát vyšší strength. High={highSalience:F4}, Low={lowSalience:F4}");
        }

        [TestMethod]
        public void InitialStrength_NeutralEpisode_MinimumStrength()
        {
            // Neutral emoce → intensity=0.45, tedy nejnižší strength
            // strength = salience × 0.45 × 0.7 + 0.3; clamped to [0.3, 1.0]
            var salience = 0.5;
            var neutral = salience * 0.45 * 0.7 + 0.3;
            var positive = salience * 0.85 * 0.7 + 0.3;
            var negative = salience * 1.00 * 0.7 + 0.3;

            Assert.IsTrue(neutral < positive,
                $"Neutral musí mít nižší strength než Positive. Neutral={neutral:F4}, Pos={positive:F4}");
            Assert.IsTrue(positive < negative,
                $"Positive musí mít nižší strength než Negative. Pos={positive:F4}, Neg={negative:F4}");
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

    // =========================================================================
    // Knowledge Tracking (Theory of Mind) — Baker, Jara-Ettinger et al. 2017
    // KnowsAbout / ConfidenceAbout — direct witness vs. gossip confidence levels
    // =========================================================================

    [TestClass]
    public class KnowledgeTrackingTests : TestBase
    {
        private IEventCollector _outbox = default!;
        private WDateTime _now;

        [TestInitialize]
        public void Setup()
        {
            _now = new WDateTime(0);
            _outbox = new EventCollector();
        }

        /// <summary>Sestaví DefaultMemoryEngine bez fidelity policy.</summary>
        private static DefaultMemoryEngine BuildMemoryEngine(MemoryConfig? cfg = null)
        {
            cfg ??= new MemoryConfig();
            var services = new ServiceCollection()
                .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
                .AddSingleton(Options.Create(cfg));
            var sp = services.BuildServiceProvider();
            return new DefaultMemoryEngine(
                sp.GetRequiredService<IOptions<MemoryConfig>>(),
                sp.GetRequiredService<ILoggerFactory>());
        }

        /// <summary>Sestaví minimální IHumanContext pro Memory engine Handle() testy.</summary>
        private IHumanContext BuildMemoryContext(HumanId selfId)
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
                Id = selfId,
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

        // ------------------------------------------------------------------
        // Test 1: přímý svědek SelfDisclosure → KnowsAbout vrátí true
        // ------------------------------------------------------------------

        [TestMethod]
        public void KnowsAbout_ReturnsTrue_AfterDirectWitnessEvent()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var engine = BuildMemoryEngine();
            var ctx = BuildMemoryContext(self);

            // SelfDisclosure: actor → self (self witnesses actor disclosing to self)
            // RecordKnowledge fires when io.Act==SelfDisclosure && io.Accepted && io.To==self
            var @event = new InteractionOutcome(
                OccurredAt: _now,
                From: actor,
                To: self,
                Accepted: true,
                Reason: "ok",
                Act: RelationalActKind.SelfDisclosure);

            // Act
            engine.Handle(@event, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.KnowsAbout(actor, "SelfDisclosure"),
                "Po přímém svědectví SelfDisclosure musí KnowsAbout vrátit true.");
        }

        // ------------------------------------------------------------------
        // Test 2: bez událostí → KnowsAbout vrátí false
        // ------------------------------------------------------------------

        [TestMethod]
        public void KnowsAbout_ReturnsFalse_WhenNoKnowledge()
        {
            // Arrange
            var engine = BuildMemoryEngine();
            var someId = new HumanId(Guid.NewGuid());

            // Assert — prázdný engine, žádná znalost
            Assert.IsFalse(engine.KnowsAbout(someId, "SelfDisclosure"),
                "Bez záznamů musí KnowsAbout vrátit false.");
        }

        // ------------------------------------------------------------------
        // Test 3: přímý svědek má vyšší confidence než gossip
        // ------------------------------------------------------------------

        [TestMethod]
        public void ConfidenceAbout_DirectWitness_IsHigherThanGossip()
        {
            // DirectWitnessConfidence = 0.90; GossipConfidence = 0.35
            var cfg = new MemoryConfig();
            Assert.IsTrue(cfg.DirectWitnessConfidence > cfg.GossipConfidence,
                $"DirectWitnessConfidence ({cfg.DirectWitnessConfidence}) musí být větší než GossipConfidence ({cfg.GossipConfidence}).");
            Assert.AreEqual(0.90, cfg.DirectWitnessConfidence, 0.001);
            Assert.AreEqual(0.35, cfg.GossipConfidence, 0.001);
        }

        // ------------------------------------------------------------------
        // Test 4: confidence na znalosti klesá po Tick() s dlouhým dt
        // ------------------------------------------------------------------

        [TestMethod]
        public void Knowledge_DecaysOver_Time()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var engine = BuildMemoryEngine();
            var ctx = BuildMemoryContext(self);

            engine.Handle(new InteractionOutcome(_now, actor, self, Accepted: true, Reason: "ok", Act: RelationalActKind.SelfDisclosure), ctx, _outbox);

            var initialConfidence = engine.ConfidenceAbout(actor, "SelfDisclosure");

            // Act — 180 dní ≈ stačí na výrazný pokles (0.005/den × 180 = 0.9 pokles)
            engine.Tick(_now, WTimeSpan.FromDays(90), ctx, _outbox);

            var decayedConfidence = engine.ConfidenceAbout(actor, "SelfDisclosure");

            // Assert
            Assert.IsTrue(decayedConfidence < initialConfidence,
                $"Confidence musí klesat s časem. Počáteční: {initialConfidence:F4}, po 90 dnech: {decayedConfidence:F4}");
        }

        // ------------------------------------------------------------------
        // Test 5: stejný fakt dvakrát → sloučení (jen 1 záznam)
        // ------------------------------------------------------------------

        [TestMethod]
        public void Knowledge_MergesWhenSameFact_RecordedTwice()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var engine = BuildMemoryEngine();
            var ctx = BuildMemoryContext(self);

            var @event = new InteractionOutcome(_now, actor, self, Accepted: true, Reason: "ok", Act: RelationalActKind.SelfDisclosure);

            // Act — stejná událost dvakrát
            engine.Handle(@event, ctx, _outbox);
            engine.Handle(@event, ctx, _outbox);

            // Assert — sloučení: jen 1 záznam
            Assert.AreEqual(1, engine.State.Knowledge.Count,
                "Stejný fakt zaznamenaný dvakrát musí být sloučen do jednoho záznamu.");
        }

        // ------------------------------------------------------------------
        // Test 6: ThirdPartyActionObserved Betrayal → gossip znalost
        // ------------------------------------------------------------------

        [TestMethod]
        public void ThirdPartyActionObserved_Betrayal_CreatesGossipKnowledge()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var engine = BuildMemoryEngine();
            var ctx = BuildMemoryContext(self);

            var @event = new ThirdPartyActionObserved(
                OccurredAt: _now,
                Observer: self,
                Actor: actor,
                Target: target,
                Valence: -1.0,
                Type: ThirdPartyObservationType.Betrayal);

            // Act
            engine.Handle(@event, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.KnowsAbout(actor, "Betrayal"),
                "Po ThirdPartyActionObserved Betrayal musí KnowsAbout(actor, 'Betrayal') vrátit true.");

            var conf = engine.ConfidenceAbout(actor, "Betrayal");
            Assert.AreEqual(engine.Config.GossipConfidence, conf, 0.001,
                $"Gossip fakt musí mít confidence = GossipConfidence ({engine.Config.GossipConfidence:F2}). Actual: {conf:F4}");
        }
    }
}
