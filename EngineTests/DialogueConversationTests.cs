// DialogueConversationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Conversation;
    using GameEngineTools.Dialogue.Planning;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Model B — conversations: adjacency pairs (question⇒answer, disclosure⇒validation, greeting⇒
    /// greeting), turn-taking (only the party who did not just speak owes a response), idle expiry,
    /// and the planner producing the response move instead of a fresh topic.
    /// </summary>
    [TestClass]
    public class DialogueConversationTests : TestBase
    {
        private static readonly HumanId A = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        private static readonly HumanId B = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));

        private static SpeechAct Act(RelationalActKind kind, HumanId speaker, HumanId addressee, long ticks,
            DialogueDimension dims = DialogueDimension.None)
            => SpeechAct.Relational(kind, speaker, addressee, new WDateTime(ticks)) with { Dimensions = dims };

        #region Adjacency pairs

        [TestMethod]
        public void ResponseTo_Question_IsAnswerWithFeedback()
        {
            var r = AdjacencyPairResolver.ResponseTo(Act(RelationalActKind.Question, A, B, 1));
            Assert.IsNotNull(r);
            Assert.AreEqual(RelationalActKind.SmallTalk, r!.Kind);
            Assert.IsTrue(r.Dimensions.HasFlag(DialogueDimension.Feedback));
        }

        [TestMethod]
        public void ResponseTo_SelfDisclosure_IsValidation()
        {
            var r = AdjacencyPairResolver.ResponseTo(Act(RelationalActKind.SelfDisclosure, A, B, 1));
            Assert.AreEqual(RelationalActKind.Validation, r!.Kind);
        }

        [TestMethod]
        public void ResponseTo_Greeting_ReturnsGreeting()
        {
            var greeting = Act(RelationalActKind.SmallTalk, A, B, 1, DialogueDimension.SocialObligation);
            var r = AdjacencyPairResolver.ResponseTo(greeting);
            Assert.IsNotNull(r);
            Assert.AreEqual(RelationalActKind.SmallTalk, r!.Kind);
            Assert.IsTrue(r.Dimensions.HasFlag(DialogueDimension.SocialObligation));
        }

        [TestMethod]
        public void ResponseTo_PlainSmallTalk_HasNoObligedResponse()
            => Assert.IsNull(AdjacencyPairResolver.ResponseTo(Act(RelationalActKind.SmallTalk, A, B, 1)));

        [TestMethod]
        public void ResponseTo_Boundary_HasNoObligedResponse()
            => Assert.IsNull(AdjacencyPairResolver.ResponseTo(Act(RelationalActKind.Boundary, A, B, 1)));

        #endregion

        #region Coordinator — turn-taking

        [TestMethod]
        public void Coordinator_AfterOtherAsks_ResponderOwesResponse()
        {
            var coord = new ConversationCoordinator();
            var now = new WDateTime(1000);
            coord.Observe(Act(RelationalActKind.Question, A, B, 1000), A, B, now);

            Assert.IsTrue(coord.TryGetPendingResponse(B, A, new WDateTime(1100), out var pending));
            Assert.AreEqual(RelationalActKind.Question, pending.RelationalKind);
        }

        [TestMethod]
        public void Coordinator_ResponderAlreadySpoke_OwesNothing()
        {
            var coord = new ConversationCoordinator();
            coord.Observe(Act(RelationalActKind.Question, A, B, 1000), A, B, new WDateTime(1000));

            // A spoke last; A owes nothing (it is B's turn).
            Assert.IsFalse(coord.TryGetPendingResponse(A, B, new WDateTime(1100), out _));
        }

        [TestMethod]
        public void Coordinator_NonObligingAct_OwesNothing()
        {
            var coord = new ConversationCoordinator();
            coord.Observe(Act(RelationalActKind.Boundary, A, B, 1000), A, B, new WDateTime(1000));
            Assert.IsFalse(coord.TryGetPendingResponse(B, A, new WDateTime(1100), out _));
        }

        [TestMethod]
        public void Coordinator_AfterIdleTimeout_ConversationExpires()
        {
            var coord = new ConversationCoordinator(WTimeSpan.FromHours(2));
            var start = WDateTime.New(WDateOnly.New(100, 1, 1));
            coord.Observe(Act(RelationalActKind.Question, A, B, start.WorldTicks), A, B, start);

            var muchLater = new WDateTime(start.WorldTicks + WTimeSpan.FromHours(3).Ticks);
            Assert.IsFalse(coord.TryGetPendingResponse(B, A, muchLater, out _));
            Assert.AreEqual(0, coord.ActiveCount);   // stale conversation dropped
        }

        #endregion

        #region Planner response mode + exchange

        private static SpeechActRequest Request(HumanId speaker, HumanId addressee, SpeechAct? respondingTo)
            => new(RelationalActKind.SmallTalk,
                EntityRef.ForHuman(speaker, "S"), EntityRef.ForHuman(addressee, "A"),
                new WDateTime(2000), Closeness: 40, Familiarity: 40,
                Agreeableness: 0.5, Style: CommunicationStyle.Direct, Power: 0.5, RespondingTo: respondingTo);

        [TestMethod]
        public void Plan_RespondingToQuestion_ProducesFeedbackAnswer()
        {
            var planner = new DefaultSpeechActPlanner();
            var question = Act(RelationalActKind.Question, A, B, 1000);

            var answer = planner.Plan(Request(B, A, respondingTo: question));

            Assert.AreEqual(RelationalActKind.SmallTalk, answer.RelationalKind);
            Assert.IsTrue(answer.Dimensions.HasFlag(DialogueDimension.Feedback));
        }

        [TestMethod]
        public void Exchange_DisclosureThenValidationThenAck_TakesTurns()
        {
            var coord = new ConversationCoordinator();
            var planner = new DefaultSpeechActPlanner();

            // A discloses to B.
            var disclosure = Act(RelationalActKind.SelfDisclosure, A, B, 1000);
            coord.Observe(disclosure, A, B, new WDateTime(1000));

            // B's turn → owes validation.
            Assert.IsTrue(coord.TryGetPendingResponse(B, A, new WDateTime(1100), out var forB));
            var bAct = planner.Plan(Request(B, A, forB));
            Assert.AreEqual(RelationalActKind.Validation, bAct.RelationalKind);
            coord.Observe(bAct, B, A, new WDateTime(1100));

            // A's turn → validation obliges an acknowledgement.
            Assert.IsTrue(coord.TryGetPendingResponse(A, B, new WDateTime(1200), out var forA));
            var aAck = planner.Plan(Request(A, B, forA));
            Assert.AreEqual(RelationalActKind.SmallTalk, aAck.RelationalKind);
            coord.Observe(aAck, A, B, new WDateTime(1200));

            // Plain small-talk acknowledgement closes the exchange — B owes nothing further.
            Assert.IsFalse(coord.TryGetPendingResponse(B, A, new WDateTime(1300), out _));
        }

        #endregion
    }
}
