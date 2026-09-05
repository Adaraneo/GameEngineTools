// DialogueConversationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Conversation;
    using GameEngineTools.Dialogue.Planning;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;

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

        // Greeting is the one symmetric pair, so it is the one that can run away: if a return greeting
        // obliged another greeting, obligation-driven replies would ping-pong until the window expired.
        [TestMethod]
        public void ResponseTo_ReturnGreeting_ClosesThePair()
        {
            var returnGreeting = Act(
                RelationalActKind.SmallTalk, B, A, 2,
                DialogueDimension.SocialObligation | DialogueDimension.Feedback);

            Assert.IsNull(
                AdjacencyPairResolver.ResponseTo(returnGreeting),
                "a greeting already given back is a second-pair-part — it must not demand a third");
        }

        [TestMethod]
        public void ResponseTo_PlainSmallTalk_HasNoObligedResponse()
            => Assert.IsNull(AdjacencyPairResolver.ResponseTo(Act(RelationalActKind.SmallTalk, A, B, 1)));

        [TestMethod]
        public void ResponseTo_Boundary_HasNoObligedResponse()
            => Assert.IsNull(AdjacencyPairResolver.ResponseTo(Act(RelationalActKind.Boundary, A, B, 1)));

        #endregion Adjacency pairs

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

        #endregion Coordinator — turn-taking

        #region Response obligation (search by responder)

        // TryGetPendingResponse can only answer "is THIS the person I owe?", which presumes the responder
        // already decided to speak and to whom. TryGetObligation searches by responder alone — that is
        // what lets a reply be delivered because it is owed rather than by coincidence.

        [TestMethod]
        public void Coordinator_TryGetObligation_FindsPartnerWithoutBeingToldWho()
        {
            var coord = new ConversationCoordinator();
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            coord.Observe(Act(RelationalActKind.Question, A, B, now.WorldTicks), A, B, now);

            Assert.IsTrue(coord.TryGetObligation(B, now, out var other, out var pending));
            Assert.AreEqual(A, other);
            Assert.AreEqual(RelationalActKind.Question, pending.RelationalKind);
        }

        [TestMethod]
        public void Coordinator_TryGetObligation_AskerOwesNothing()
        {
            var coord = new ConversationCoordinator();
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            coord.Observe(Act(RelationalActKind.Question, A, B, now.WorldTicks), A, B, now);

            // A holds the floor — the obligation runs the other way.
            Assert.IsFalse(coord.TryGetObligation(A, now, out _, out _));
        }

        [TestMethod]
        public void Coordinator_TryGetObligation_NonObligingAct_OwesNothing()
        {
            var coord = new ConversationCoordinator();
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            coord.Observe(Act(RelationalActKind.Boundary, A, B, now.WorldTicks), A, B, now);

            Assert.IsFalse(coord.TryGetObligation(B, now, out _, out _));
        }

        [TestMethod]
        public void Coordinator_TryGetObligation_LapsesAfterReplyWindow()
        {
            var coord = new ConversationCoordinator(
                idleTimeout: WTimeSpan.FromHours(2), replyWindow: WTimeSpan.FromMinutes(15));
            var start = WDateTime.New(WDateOnly.New(100, 1, 1));
            coord.Observe(Act(RelationalActKind.Question, A, B, start.WorldTicks), A, B, start);

            var withinWindow = new WDateTime(start.WorldTicks + WTimeSpan.FromMinutes(10).Ticks);
            Assert.IsTrue(coord.TryGetObligation(B, withinWindow, out _, out _));

            // The moment to answer passes well before the conversation itself is forgotten: nobody
            // answers a question half an hour late, but the pair still count as having talked.
            var afterWindow = new WDateTime(start.WorldTicks + WTimeSpan.FromMinutes(30).Ticks);
            Assert.IsFalse(coord.TryGetObligation(B, afterWindow, out _, out _));
            Assert.IsTrue(coord.TryGetPendingResponse(B, A, afterWindow, out _));
        }

        [TestMethod]
        public void Coordinator_TryGetObligation_TwoPendingQuestions_AnswersFreshest()
        {
            var c = new HumanId(Guid.Parse("cccccccc-0000-0000-0000-000000000003"));
            var coord = new ConversationCoordinator();
            var t0 = WDateTime.New(WDateOnly.New(100, 1, 1));
            var t1 = new WDateTime(t0.WorldTicks + WTimeSpan.FromMinutes(5).Ticks);

            coord.Observe(Act(RelationalActKind.Question, A, B, t0.WorldTicks), A, B, t0);
            coord.Observe(Act(RelationalActKind.Question, c, B, t1.WorldTicks), c, B, t1);

            Assert.IsTrue(coord.TryGetObligation(B, t1, out var other, out _));
            Assert.AreEqual(c, other, "the freshest question is the one still hanging in the air");
        }

        [TestMethod]
        public void Coordinator_TryGetObligation_UninvolvedCharacter_OwesNothing()
        {
            var c = new HumanId(Guid.Parse("cccccccc-0000-0000-0000-000000000003"));
            var coord = new ConversationCoordinator();
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            coord.Observe(Act(RelationalActKind.Question, A, B, now.WorldTicks), A, B, now);

            Assert.IsFalse(coord.TryGetObligation(c, now, out _, out _));
        }

        [TestMethod]
        public void Coordinator_AnsweringFlipsTheFloor_SoTheReplyIsNotDeliveredTwice()
        {
            var coord = new ConversationCoordinator();
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));
            coord.Observe(Act(RelationalActKind.Question, A, B, now.WorldTicks), A, B, now);
            Assert.IsTrue(coord.TryGetObligation(B, now, out _, out _));

            // B answers: the reply is itself observed, so B now holds the floor and owes nothing.
            coord.Observe(Act(RelationalActKind.SmallTalk, B, A, now.WorldTicks, DialogueDimension.Feedback), B, A, now);

            Assert.IsFalse(coord.TryGetObligation(B, now, out _, out _));
        }

        #endregion Response obligation (search by responder)

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

        #endregion Planner response mode + exchange
    }
}
