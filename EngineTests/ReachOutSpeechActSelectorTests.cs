// ReachOutSpeechActSelectorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Simulation;

    /// <summary>
    /// Tests for <see cref="ReachOutSpeechActSelector"/>.
    /// </summary>
    [TestClass]
    public class ReachOutSpeechActSelectorTests
    {
        #region ReachOut routing

        /// <summary>
        /// ReachOut selection should not collapse into pure SmallTalk once some relationship context exists.
        /// </summary>
        [TestMethod]
        public void SelectSpeechAct_ModerateContext_DoesNotStayOnlySmallTalk()
        {
            var counts = SampleActs(
                BuildEdge(familiarity: 35, trust: 58, comfort: 60, closeness: 32, romanticInterest: 20),
                new InteractionSurface("Village", false, 0.2, 0.2, SurfaceKind.Social),
                draws: 256);

            Assert.IsTrue(counts.ContainsKey(SpeechAct.Question));
            Assert.IsTrue(counts.Keys.Any(a => a != SpeechAct.SmallTalk));
        }

        /// <summary>
        /// Low familiarity should overwhelmingly stay in safe openers.
        /// </summary>
        [TestMethod]
        public void SelectSpeechAct_LowFamiliarity_PrefersSmallTalkAndQuestion()
        {
            var counts = SampleActs(
                BuildEdge(familiarity: 8, trust: 50, comfort: 46, closeness: 8, romanticInterest: 5),
                new InteractionSurface("Village", false, 0.2, 0.2, SurfaceKind.Social),
                draws: 300);

            Assert.AreEqual(2, counts.Count);
            Assert.IsTrue(counts.ContainsKey(SpeechAct.SmallTalk));
            Assert.IsTrue(counts.ContainsKey(SpeechAct.Question));
            Assert.IsTrue(counts[SpeechAct.SmallTalk] > counts[SpeechAct.Question]);
        }

        /// <summary>
        /// Question should unlock at earlier familiarity/comfort than before.
        /// </summary>
        [TestMethod]
        public void SelectSpeechAct_Question_UnlocksAtEarlierThreshold()
        {
            var counts = SampleActs(
                BuildEdge(familiarity: 8, trust: 50, comfort: 47, closeness: 6, romanticInterest: 5),
                new InteractionSurface("Village", false, 0.2, 0.2, SurfaceKind.Social),
                draws: 240);

            Assert.IsTrue(counts.ContainsKey(SpeechAct.Question));
            Assert.IsTrue(counts[SpeechAct.Question] > 0);
        }

        /// <summary>
        /// Higher trust and comfort should unlock warmer acts beyond safe openers.
        /// </summary>
        [TestMethod]
        public void SelectSpeechAct_HigherTrustAndComfort_CanProduceValidationDisclosureAndMeta()
        {
            var counts = SampleActs(
                BuildEdge(familiarity: 55, trust: 78, comfort: 76, closeness: 62, romanticInterest: 45),
                new InteractionSurface("Room", true, 0.1, 0.1, SurfaceKind.Private),
                draws: 600);

            Assert.IsTrue(counts.ContainsKey(SpeechAct.Validation));
            Assert.IsTrue(counts.ContainsKey(SpeechAct.SelfDisclosure));
            Assert.IsTrue(counts.ContainsKey(SpeechAct.Meta));
        }

        /// <summary>
        /// Self-disclosure should already be reachable in a moderate, not only very deep, relationship state.
        /// </summary>
        [TestMethod]
        public void SelectSpeechAct_SelfDisclosure_IsReachableAtModerateRelationshipState()
        {
            var counts = SampleActs(
                BuildEdge(familiarity: 24, trust: 54, comfort: 52, closeness: 18, romanticInterest: 12),
                new InteractionSurface("Village", false, 0.2, 0.2, SurfaceKind.Social),
                draws: 320);

            Assert.IsTrue(counts.ContainsKey(SpeechAct.SelfDisclosure));
        }

        /// <summary>
        /// Invite should remain available only in strong contexts and still be relatively rare.
        /// </summary>
        [TestMethod]
        public void SelectSpeechAct_Invite_RemainsRareAndConditioned()
        {
            var weakCounts = SampleActs(
                BuildEdge(familiarity: 50, trust: 60, comfort: 57, closeness: 34, romanticInterest: 28),
                new InteractionSurface("Village", false, 0.2, 0.2, SurfaceKind.Social),
                draws: 400);

            Assert.IsFalse(weakCounts.ContainsKey(SpeechAct.Invite));

            var strongCounts = SampleActs(
                BuildEdge(familiarity: 70, trust: 82, comfort: 80, closeness: 72, romanticInterest: 78),
                new InteractionSurface("Room", true, 0.1, 0.1, SurfaceKind.Private),
                draws: 600);

            Assert.IsTrue(strongCounts.ContainsKey(SpeechAct.Invite));
            Assert.IsTrue(strongCounts[SpeechAct.Invite] < strongCounts[SpeechAct.Validation]);
            Assert.IsTrue(strongCounts[SpeechAct.Invite] < strongCounts[SpeechAct.SelfDisclosure]);
        }

        #endregion ReachOut routing

        #region Touch gating

        /// <summary>
        /// Light touch should become reachable earlier than friendly touch.
        /// </summary>
        [TestMethod]
        public void TouchSelector_LightTouch_IsReachableEarlierThanFriendlyTouch()
        {
            var earlyEdge = BuildEdge(familiarity: 20, trust: 50, comfort: 49, closeness: 21, romanticInterest: 8) with
            {
                SexualInterest = 10
            };

            Assert.IsTrue(ReachOutTouchSelector.CanAttemptLightTouch(earlyEdge));
            Assert.IsFalse(ReachOutTouchSelector.CanAttemptFriendlyTouch(earlyEdge, hasPrivacy: true));
        }

        /// <summary>
        /// Friendly touch should still require a warmer and more private context than light touch.
        /// </summary>
        [TestMethod]
        public void TouchSelector_FriendlyTouch_StaysMoreRestrictedThanLightTouch()
        {
            var warmEdge = BuildEdge(familiarity: 40, trust: 60, comfort: 58, closeness: 45, romanticInterest: 20) with
            {
                SexualInterest = 24
            };

            Assert.IsTrue(ReachOutTouchSelector.CanAttemptLightTouch(warmEdge));
            Assert.IsFalse(ReachOutTouchSelector.CanAttemptFriendlyTouch(warmEdge, hasPrivacy: false));
            Assert.IsTrue(ReachOutTouchSelector.CanAttemptFriendlyTouch(warmEdge, hasPrivacy: true));
        }

        #endregion Touch gating

        #region Helpers

        private static Dictionary<SpeechAct, int> SampleActs(
            RelationshipEdge edge,
            InteractionSurface surface,
            int draws)
        {
            var rng = new Random(12345);
            var counts = new Dictionary<SpeechAct, int>();

            for (var i = 0; i < draws; i++)
            {
                var act = ReachOutSpeechActSelector.SelectSpeechAct(edge, surface, rng).Act;
                counts[act] = counts.TryGetValue(act, out var count) ? count + 1 : 1;
            }

            return counts;
        }

        private static RelationshipEdge BuildEdge(
            double familiarity,
            double trust,
            double comfort,
            double closeness,
            double romanticInterest)
            => new(
                A: default,
                B: default,
                Like: 55,
                Trust: trust,
                Familiarity: familiarity,
                AestheticAttraction: 55,
                PhysicalAttraction: 55,
                RomanticInterest: romanticInterest,
                SexualInterest: 20,
                Closeness: closeness,
                Respect: 55,
                Comfort: comfort,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                PositiveInteractionCount: 3);

        #endregion Helpers
    }
}
