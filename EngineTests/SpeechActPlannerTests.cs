// SpeechActPlannerTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Planning;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Phase-3 planner: register follows relationship state, directness follows a Brown &amp; Levinson
    /// face-threat score, the result is deterministic, and every act is fully specified (speaker,
    /// addressee, roles) for direct-address (mode-2) realisation.
    /// </summary>
    [TestClass]
    public class SpeechActPlannerTests
    {
        private static readonly EntityRef Petr = EntityRef.ForHuman(new HumanId(Guid.Parse("11111111-1111-1111-1111-111111111111")), "Petr");
        private static readonly EntityRef Jana = EntityRef.ForHuman(new HumanId(Guid.Parse("22222222-2222-2222-2222-222222222222")), "Jana");

        private static SpeechActRequest Request(
            RelationalActKind intent = RelationalActKind.SmallTalk,
            double closeness = 40,
            double familiarity = 40,
            double agreeableness = 0.5,
            CommunicationStyle style = CommunicationStyle.LowContext,
            double power = 0.5,
            double urgency = 0.0,
            bool ironic = false)
            => new(intent, Petr, Jana, new WDateTime(1000), closeness, familiarity, agreeableness, style, power, urgency, ironic);

        [DataTestMethod]
        [DataRow(5.0, 80.0, Register.Formal)]     // low familiarity ⇒ social distance
        [DataRow(50.0, 80.0, Register.Intimate)]  // familiar + very close
        [DataRow(50.0, 20.0, Register.Informal)]  // familiar but not close
        public void Plan_Register_FollowsClosenessAndFamiliarity(double familiarity, double closeness, Register expected)
        {
            var planner = new DefaultSpeechActPlanner();
            var act = planner.Plan(Request(closeness: closeness, familiarity: familiarity));
            Assert.AreEqual(expected, act.Register);
        }

        [DataTestMethod]
        [DataRow(0.1, CommunicationStyle.Direct, 0.9, Directness.Blunt)]      // disagreeable, direct, powerful
        [DataRow(0.9, CommunicationStyle.Indirect, 0.1, Directness.Indirect)] // agreeable, indirect, low power
        [DataRow(0.8, CommunicationStyle.Direct, 0.5, Directness.Neutral)]    // terms cancel out
        public void Plan_Directness_FollowsFaceThreatScore(
            double agreeableness, CommunicationStyle style, double power, Directness expected)
        {
            var planner = new DefaultSpeechActPlanner();
            var act = planner.Plan(Request(agreeableness: agreeableness, style: style, power: power));
            Assert.AreEqual(expected, act.Directness);
        }

        [TestMethod]
        public void Plan_UrgencyPushesTowardBlunt()
        {
            // Baseline (A=0.8, Direct, power=0.5) scores 0.0 → Neutral; urgency then tips it to Blunt.
            var planner = new DefaultSpeechActPlanner();
            var calm = planner.Plan(Request(agreeableness: 0.8, style: CommunicationStyle.Direct, power: 0.5, urgency: 0.0));
            var urgent = planner.Plan(Request(agreeableness: 0.8, style: CommunicationStyle.Direct, power: 0.5, urgency: 1.0));

            Assert.AreEqual(Directness.Neutral, calm.Directness);
            Assert.AreEqual(Directness.Blunt, urgent.Directness);
        }

        [DataTestMethod]
        [DataRow(RelationalActKind.Invite, IllocutionaryPoint.Directive)]
        [DataRow(RelationalActKind.SelfDisclosure, IllocutionaryPoint.Assertive)]
        [DataRow(RelationalActKind.Question, IllocutionaryPoint.Question)]
        public void Plan_IllocutionaryPoint_ComesFromChosenPredicate(RelationalActKind intent, IllocutionaryPoint expected)
        {
            var planner = new DefaultSpeechActPlanner();
            var act = planner.Plan(Request(intent: intent));
            Assert.AreEqual(expected, act.Point);
        }

        [DataTestMethod]
        [DataRow(0.95, 0.1, "vyžadovat")]   // powerful + disagreeable → demand
        [DataRow(0.5, 0.5, "požádat")]      // neutral → deferential request
        [DataRow(0.1, 0.9, "žebrat o")]     // powerless + agreeable → beg
        public void Plan_Request_PredicateReflectsSpeakerPower(double power, double agreeableness, string expectedLemma)
        {
            var planner = new DefaultSpeechActPlanner();
            var act = planner.Plan(Request(intent: RelationalActKind.Request, power: power, agreeableness: agreeableness));
            Assert.AreEqual(expectedLemma, act.PredicateLemma);
        }

        [TestMethod]
        public void Plan_Request_UrgencyPushesLowPowerSpeakerToBeg()
        {
            var planner = new DefaultSpeechActPlanner();
            // A mildly low-power speaker requests politely when calm, but pleads when it is urgent.
            var calm = planner.Plan(Request(intent: RelationalActKind.Request, power: 0.3, agreeableness: 0.7, urgency: 0.0));
            var urgent = planner.Plan(Request(intent: RelationalActKind.Request, power: 0.3, agreeableness: 0.7, urgency: 1.0));

            Assert.AreEqual("požádat", calm.PredicateLemma);
            Assert.AreEqual("žebrat o", urgent.PredicateLemma);
        }

        [TestMethod]
        public void Plan_FillsSpeakerAddresseeAndActorRole()
        {
            var planner = new DefaultSpeechActPlanner();
            var act = planner.Plan(Request(intent: RelationalActKind.Invite));

            Assert.AreEqual(Petr, act.Speaker);
            Assert.AreEqual(Jana, act.Addressee);
            Assert.AreEqual(Petr, act.Roles[FgdFunctor.ACT]);
            // Invite (pozvat/navrhnout) binds the addressee as PAT or ADDR — either way it is present.
            Assert.IsTrue(act.Roles.ContainsKey(FgdFunctor.PAT) || act.Roles.ContainsKey(FgdFunctor.ADDR));
            Assert.IsFalse(string.IsNullOrEmpty(act.PredicateLemma));
        }

        [TestMethod]
        public void Plan_NonIronic_HasNoForceShift_IronicHasOne()
        {
            var planner = new DefaultSpeechActPlanner();
            Assert.IsNull(planner.Plan(Request(ironic: false)).ForceShift);
            Assert.IsNotNull(planner.Plan(Request(ironic: true)).ForceShift);
        }

        [TestMethod]
        public void Plan_IsDeterministic_SameRequestYieldsIdenticalAct()
        {
            var planner = new DefaultSpeechActPlanner();
            var request = Request(intent: RelationalActKind.Validation, closeness: 70, familiarity: 40, agreeableness: 0.3, power: 0.7);

            var a = planner.Plan(request);
            var b = planner.Plan(request);

            Assert.AreEqual(a.Point, b.Point);
            Assert.AreEqual(a.RelationalKind, b.RelationalKind);
            Assert.AreEqual(a.PredicateLemma, b.PredicateLemma);
            Assert.AreEqual(a.Register, b.Register);
            Assert.AreEqual(a.Directness, b.Directness);
            Assert.AreEqual(a.Polarity, b.Polarity);
            Assert.AreEqual(a.ForceShift, b.ForceShift);
            Assert.AreEqual(a.Speaker, b.Speaker);
            Assert.AreEqual(a.Addressee, b.Addressee);
            Assert.AreEqual(a.OccurredAt, b.OccurredAt);
            CollectionAssert.AreEquivalent(a.Roles.ToList(), b.Roles.ToList());
        }
    }
}
