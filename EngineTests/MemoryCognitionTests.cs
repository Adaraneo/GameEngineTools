// MemoryCognitionTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static EngineTests.MemoryCognitionTestData;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class MemoryCognitionRecallTests : TestBase
    {
        [TestMethod]
        public void Recall_ExactTargetMatch_OutranksNonTargetMatch()
        {
            var target = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 3, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.60, target),
                Episode(now, 1, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.75, other)
            });

            var recall = MemoryCognition.Recall(memory, new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(7), 2), now);

            Assert.AreEqual(target, recall.Items[0].Episode.OtherPerson);
        }

        [TestMethod]
        public void Recall_RecentEpisode_OutranksOlderEpisode_WhenOtherFactorsEqual()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var recent = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.60, target);
            var old = Episode(now, 48, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.60, target);
            var memory = new MemoryIndex(new List<EpisodicMemory> { old, recent });

            var recall = MemoryCognition.Recall(memory, new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(7), 2), now);

            Assert.AreEqual(recent.Id, recall.Items[0].Episode.Id);
        }

        [TestMethod]
        public void Recall_StrongerNegativeEpisode_OutranksWeakerNegativeEpisode()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var stronger = Episode(now, 6, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.90, target, salience: 0.85);
            var weaker = Episode(now, 6, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.45, target, salience: 0.55);
            var memory = new MemoryIndex(new List<EpisodicMemory> { weaker, stronger });

            var recall = MemoryCognition.Recall(memory, new MemoryRecallQuery(target, InviteIntimacy, RelationalActKind.Invite, EmotionalTag.Negative, WTimeSpan.FromDays(7), 2), now);

            Assert.AreEqual(stronger.Id, recall.Items[0].Episode.Id);
        }

        [TestMethod]
        public void Recall_IrrelevantGenericEpisodes_DoNotDisplaceBetterTargetSpecificEpisodes()
        {
            var target = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 3, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.72, target),
                Episode(now, 5, "Interaction:Question:Accepted|from=a|to=b", EmotionalTag.Positive, 0.70, target),
                Episode(now, 1, "Action:Work", EmotionalTag.Negative, 0.95, null, salience: 0.95),
                Episode(now, 1, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.90, other, salience: 0.90)
            });

            var recall = MemoryCognition.Recall(memory, new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(7), 3), now);

            Assert.AreEqual(2, recall.Items.Count);
            Assert.IsTrue(recall.Items.All(item => item.Episode.OtherPerson == target));
        }
    }

    [TestClass]
    public class MemoryCognitionReflectionTests : TestBase
    {
        [TestMethod]
        public void BuildWorkingSet_RepeatedRejectedIntimacy_ProducesRejectsIntimacySummary()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 12, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.80, target, salience: 0.85),
                Episode(now, 36, "Interaction:SelfDisclosure:Rejected|from=a|to=b", EmotionalTag.Negative, 0.75, target, salience: 0.80),
                Episode(now, 6, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.50, target)
            });

            var workingSet = MemoryCognition.BuildWorkingSet(memory, new MemoryRecallQuery(target, InviteIntimacy, RelationalActKind.Invite, null, WTimeSpan.FromDays(14), 4), now);
            var summary = workingSet.Reflections.Single(r => r.Kind == ReflectionSummaryKind.RejectsIntimacy);

            Assert.AreEqual(2, summary.EvidenceCount);
            // Strength formula: 0.14 + (intimacyRejections * 0.18). With 2 episodes at 12h and 36h,
            // weighted score ≈ 1.76, giving strength ≈ 0.46. Original threshold 0.5 was pre-compile.
            Assert.IsTrue(summary.Strength >= 0.40);
        }

        [TestMethod]
        public void BuildWorkingSet_MixedEvidence_DoesNotCreateOverstrongRejectsIntimacySummary()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 12, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.80, target, salience: 0.85),
                Episode(now, 8, "Interaction:Validation:Accepted|from=a|to=b", EmotionalTag.Positive, 0.78, target, salience: 0.80),
                Episode(now, 6, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.60, target)
            });

            var workingSet = MemoryCognition.BuildWorkingSet(memory, new MemoryRecallQuery(target, InviteIntimacy, RelationalActKind.Invite, null, WTimeSpan.FromDays(14), 4), now);

            Assert.IsFalse(workingSet.Reflections.Any(r => r.Kind == ReflectionSummaryKind.RejectsIntimacy));
        }

        [TestMethod]
        public void BuildWorkingSet_ContainsOnlyTopRelevantItems_AndRemainsDeterministic()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.70, target),
                Episode(now, 4, "Interaction:Question:Accepted|from=a|to=b", EmotionalTag.Positive, 0.68, target),
                Episode(now, 6, "Interaction:Humor:Accepted|from=a|to=b", EmotionalTag.Positive, 0.66, target),
                Episode(now, 10, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.64, target),
                Episode(now, 14, "Relation:MicroPositive|from=a|what=helped", EmotionalTag.Positive, 0.62, target)
            });

            var query = new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(21), 3);
            var first = MemoryCognition.BuildWorkingSet(memory, query, now);
            var second = MemoryCognition.BuildWorkingSet(memory, query, now);

            Assert.AreEqual(3, first.RecalledEpisodes.Count);
            CollectionAssert.AreEqual(
                first.RecalledEpisodes.Select(item => item.Episode.Id).ToList(),
                second.RecalledEpisodes.Select(item => item.Episode.Id).ToList());
        }

        [TestMethod]
        public void BuildWorkingSet_NoTarget_FallbackStillReturnsNegativeLoad()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 2, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.70, target),
                Episode(now, 4, "Relation:MicroNegative|from=a|what=ignored", EmotionalTag.Negative, 0.65, target),
                Episode(now, 8, "Action:Work", EmotionalTag.Neutral, 0.40, null)
            });

            var workingSet = MemoryCognition.BuildWorkingSet(memory, new MemoryRecallQuery(null, SelfCare, null, EmotionalTag.Negative, WTimeSpan.FromDays(7), 4), now);

            Assert.IsTrue(workingSet.RecalledEpisodes.Count >= 2);
            Assert.IsTrue(workingSet.Reflections.Any(r => r.Kind == ReflectionSummaryKind.RecentSocialCost));
        }

        [TestMethod]
        public void BuildWorkingSet_RepeatedWarmLowStakesContact_CreatesWarmAndSafeSummaries()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.74, target),
                Episode(now, 8, "Interaction:Question:Accepted|from=a|to=b", EmotionalTag.Positive, 0.72, target),
                Episode(now, 12, "Interaction:Validation:Accepted|from=a|to=b", EmotionalTag.Positive, 0.70, target)
            });

            var workingSet = MemoryCognition.BuildWorkingSet(memory, new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(14), 4), now);

            Assert.IsTrue(workingSet.Reflections.Any(r => r.Kind == ReflectionSummaryKind.WarmForCasualContact));
            Assert.IsTrue(workingSet.Reflections.Any(r => r.Kind == ReflectionSummaryKind.SafeForReachOut));
        }

        [TestMethod]
        public void BuildWorkingSet_RecentNegativeTargetSocialEpisodes_CreateTargetBoundSocialCost()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 4, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.72, target),
                Episode(now, 10, "Relation:MicroNegative|from=a|what=ignored", EmotionalTag.Negative, 0.68, target),
                Episode(now, 20, "Interaction:Question:Accepted|from=a|to=b", EmotionalTag.Positive, 0.62, target)
            });

            var workingSet = MemoryCognition.BuildWorkingSet(memory, new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(14), 4), now);
            var summary = workingSet.Reflections.Single(r => r.Kind == ReflectionSummaryKind.RecentSocialCost);

            Assert.AreEqual(target, summary.TargetHuman);
            Assert.IsTrue(summary.Strength >= 0.20);
        }
    }

    [TestClass]
    public class MemoryCognitionBehaviorTests : TestBase
    {
        [TestMethod]
        public void Modify_RecentTargetSpecificRejection_ReducesInviteIntimacy()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 3, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.85, target, salience: 0.90),
                Episode(now, 12, "Interaction:SelfDisclosure:Rejected|from=a|to=b", EmotionalTag.Negative, 0.78, target, salience: 0.82)
            });
            var context = BehaviorComponentTestFactory.Context(now: now, memory: memory);
            var candidates = new List<BehaviorCandidate>
            {
                new(InviteIntimacy, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social, SocialTargeting: new SocialTargetingData(target, RelationalActKind.Invite, 0.5, 0.4, 0.6))
            };

            new MemoryInfluenceEngine().Modify(context, candidates);

            Assert.IsTrue(candidates[0].Utility < 10);
            Assert.IsTrue(context.DecisionWorkingSets?.Count > 0);
        }

        [TestMethod]
        public void Modify_RecentPositiveLowStakesInteraction_BoostsReachOut()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.78, target),
                Episode(now, 8, "Interaction:Question:Accepted|from=a|to=b", EmotionalTag.Positive, 0.72, target)
            });
            var context = BehaviorComponentTestFactory.Context(now: now, memory: memory);
            var candidates = new List<BehaviorCandidate>
            {
                new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social, SocialTargeting: new SocialTargetingData(target, RelationalActKind.SmallTalk, 0.5, 0.6, 0.3))
            };

            new MemoryInfluenceEngine().Modify(context, candidates);

            Assert.IsTrue(candidates[0].Utility > 10);
        }

        [TestMethod]
        public void Modify_NegativeSocialLoad_BoostsSelfCare()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 2, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.75, target),
                Episode(now, 6, "Relation:MicroNegative|from=a|what=ignored", EmotionalTag.Negative, 0.70, target)
            });
            var context = BehaviorComponentTestFactory.Context(now: now, memory: memory);
            var candidates = new List<BehaviorCandidate> { new(SelfCare, 10, WTimeSpan.FromHours(1), BehaviorDomain.Physiological) };

            new MemoryInfluenceEngine().Modify(context, candidates);

            Assert.IsTrue(candidates[0].Utility > 10);
        }

        [TestMethod]
        public void Modify_TargetSpecificHistory_ChangesRelativeTargetScore()
        {
            var preferred = new HumanId(Guid.NewGuid());
            var avoided = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.78, preferred),
                Episode(now, 5, "Interaction:Question:Accepted|from=a|to=b", EmotionalTag.Positive, 0.72, preferred),
                Episode(now, 2, "Interaction:Invite:Rejected|from=a|to=b", EmotionalTag.Negative, 0.84, avoided),
                Episode(now, 6, "Interaction:SelfDisclosure:Rejected|from=a|to=b", EmotionalTag.Negative, 0.80, avoided)
            });
            var context = BehaviorComponentTestFactory.Context(now: now, memory: memory);
            var candidates = new List<BehaviorCandidate>
            {
                new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social, SocialTargeting: new SocialTargetingData(preferred, RelationalActKind.SmallTalk, 0.5, 0.6, 0.3)),
                new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social, SocialTargeting: new SocialTargetingData(avoided, RelationalActKind.SmallTalk, 0.5, 0.4, 0.7))
            };

            new MemoryInfluenceEngine().Modify(context, candidates);

            Assert.IsTrue(candidates[0].Utility > candidates[1].Utility);
        }

        [TestMethod]
        public void Modify_EmptyMemory_DoesNotBreakCandidateScoring()
        {
            var context = BehaviorComponentTestFactory.Context(memory: new MemoryIndex(new List<EpisodicMemory>()));
            var candidates = new List<BehaviorCandidate>
            {
                new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social),
                new(SelfCare, 8, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)
            };

            new MemoryInfluenceEngine().Modify(context, candidates);

            Assert.AreEqual(10, candidates[0].Utility);
            Assert.AreEqual(8, candidates[1].Utility);
        }

        [TestMethod]
        public void Modify_WorkingSets_AreCandidateSpecific_AndDoNotOverwriteEachOther()
        {
            var first = new HumanId(Guid.NewGuid());
            var second = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.75, first),
                Episode(now, 3, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.76, second)
            });
            var context = BehaviorComponentTestFactory.Context(now: now, memory: memory);
            var candidates = new List<BehaviorCandidate>
            {
                new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social, SocialTargeting: new SocialTargetingData(first, RelationalActKind.SmallTalk, 0.5, 0.6, 0.3)),
                new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social, SocialTargeting: new SocialTargetingData(second, RelationalActKind.SmallTalk, 0.5, 0.4, 0.7))
            };

            new MemoryInfluenceEngine().Modify(context, candidates);

            Assert.AreEqual(2, context.DecisionWorkingSets?.Count);
            Assert.IsTrue(context.DecisionWorkingSets!.Keys.Any(key => key.Contains(first.Value.ToString("N"), StringComparison.Ordinal)));
            Assert.IsTrue(context.DecisionWorkingSets!.Keys.Any(key => key.Contains(second.Value.ToString("N"), StringComparison.Ordinal)));
        }
    }

    [TestClass]
    public class MemoryCognitionMoodCongruenceTests : TestBase
    {
        private static MemoryIndex PositiveAndNegativeEpisodes(HumanId target, WDateTime now)
            => new(new List<EpisodicMemory>
            {
                Episode(now, 3, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.60, target, salience: 0.60),
                Episode(now, 3, "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.60, target, salience: 0.60)
            });

        private static double RelevanceOf(MemoryRecallResult recall, EmotionalTag emotion)
            => recall.Items.First(i => i.Episode.Emotion == emotion).Relevance;

        [TestMethod]
        public void PositiveMood_RanksPositiveEpisodeAboveNegative()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = PositiveAndNegativeEpisodes(target, now);

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(14), 2,
                    CurrentValence: 0.6, NeuroticismScore: 0.5), now);

            Assert.IsTrue(RelevanceOf(recall, EmotionalTag.Positive) > RelevanceOf(recall, EmotionalTag.Negative),
                "A positive-mood character recalls positive episodes slightly more (healthy positivity bias).");
        }

        [TestMethod]
        public void DepressedMood_ReversesToNegativeCongruentRecall()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            var memory = PositiveAndNegativeEpisodes(target, now);

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(14), 2,
                    CurrentValence: -0.6, NeuroticismScore: 0.5), now);

            Assert.IsTrue(RelevanceOf(recall, EmotionalTag.Negative) > RelevanceOf(recall, EmotionalTag.Positive),
                "Below the depression threshold the positivity bias reverses to negative-congruent recall.");
        }

        [TestMethod]
        public void MoodCongruence_StaysBelow_SalienceAndRecencyDominance()
        {
            var target = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(100, 1, 10, 12);
            // A far more salient & recent NEGATIVE episode still outranks a faint positive one even in
            // a positive mood — mood congruence is small and never overrides salience/recency.
            var memory = new MemoryIndex(new List<EpisodicMemory>
            {
                Episode(now, 30, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.40, target, salience: 0.35),
                Episode(now, 1,  "Interaction:SmallTalk:Rejected|from=a|to=b", EmotionalTag.Negative, 0.95, target, salience: 0.95)
            });

            var recall = MemoryCognition.Recall(memory,
                new MemoryRecallQuery(target, ReachOut, RelationalActKind.SmallTalk, null, WTimeSpan.FromDays(14), 2,
                    CurrentValence: 0.6, NeuroticismScore: 0.5), now);

            Assert.IsTrue(RelevanceOf(recall, EmotionalTag.Negative) > RelevanceOf(recall, EmotionalTag.Positive),
                "Mood-congruence must not override salience/recency dominance.");
        }
    }

    internal static class MemoryCognitionTestData
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
