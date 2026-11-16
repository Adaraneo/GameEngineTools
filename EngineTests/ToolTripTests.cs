// ToolTripTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Simulation;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public sealed class ToolTripTests
    {
        private static Personality Person(double conscientiousness, double neuroticism)
            => new(
                BigFive: new BigFive(0.5, conscientiousness, 0.5, 0.5, neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

        [TestMethod]
        public void ADiligentCalmPerson_WalksFartherThanAnAnxiousOne()
        {
            var diligent = ToolTrip.MaxOneWayHours(Person(0.9, 0.1), 10, 20, 20);
            var anxious = ToolTrip.MaxOneWayHours(Person(0.1, 0.9), 10, 20, 20);

            Assert.IsTrue(diligent > anxious);
        }

        [TestMethod]
        public void StressAndNeedPressure_ShortenTheWalk()
        {
            var rested = ToolTrip.MaxOneWayHours(Person(0.5, 0.5), 0, 10, 10);

            Assert.IsTrue(ToolTrip.MaxOneWayHours(Person(0.5, 0.5), 80, 10, 10) < rested, "Stress.");
            Assert.IsTrue(ToolTrip.MaxOneWayHours(Person(0.5, 0.5), 0, 60, 10) < rested, "Hunger.");
        }

        [TestMethod]
        public void NobodyWalksWhereTheRoundTripEndsPastTheEmergencyLevel()
        {
            Assert.AreEqual(0.0, ToolTrip.MaxOneWayHours(Person(0.9, 0.1), 0, 80, 20), "Already past the emergency level: stay and eat.");

            var nearlyThere = ToolTrip.MaxOneWayHours(Person(0.9, 0.1), 0, 60, 20);
            Assert.IsTrue(nearlyThere <= (75.0 - 60.0) / 6.0 / 2.0 + 1e-9, "The body budget caps the walk whatever the person would be willing to do.");
        }

        [TestMethod]
        public void TheRetryPauseDoublesWithEveryFailureInARow_UpToACap()
        {
            Assert.AreEqual(6.0, ToolTrip.RetryHours(1));
            Assert.AreEqual(12.0, ToolTrip.RetryHours(2));
            Assert.AreEqual(24.0, ToolTrip.RetryHours(3));
            Assert.AreEqual(48.0, ToolTrip.RetryHours(4));
            Assert.AreEqual(48.0, ToolTrip.RetryHours(9));
        }
    }
}
