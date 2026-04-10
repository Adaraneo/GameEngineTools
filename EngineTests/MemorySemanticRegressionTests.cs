// MemorySemanticRegressionTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.World.Utils.Time;
    using System;
    using static GameEngineTools.Characters.Engines.Memory.MemoryWhatParser;

    [TestClass]
    public class MemorySemanticRegressionTests
    {
        #region Reinforcement

        [TestMethod]
        public void ReinforcementKey_SameInteractionMeaningAndSameOther_IsEqual()
        {
            // Arrange
            var other = new HumanId(Guid.NewGuid());

            var first = new EpisodicMemory(
                Guid.NewGuid(),
                new WDateTime(1),
                "Interaction:Invite:Accepted|from=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa|to=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                0.70,
                EmotionalTag.Positive,
                0.50,
                OtherPerson: other);

            var second = new EpisodicMemory(
                Guid.NewGuid(),
                new WDateTime(2),
                "Interaction:Invite:Accepted|from=cccccccc-cccc-cccc-cccc-cccccccccccc|to=dddddddd-dddd-dddd-dddd-dddddddddddd",
                0.70,
                EmotionalTag.Positive,
                0.50,
                OtherPerson: other);

            // Act
            var keyFirst = MemoryReinforcementKeyBuilder.From(first);
            var keySecond = MemoryReinforcementKeyBuilder.From(second);

            // Assert
            Assert.AreEqual(keyFirst, keySecond);
        }

        [TestMethod]
        public void ReinforcementKey_MicroPositiveDifferentWhat_IsNotEqual()
        {
            // Arrange
            var other = new HumanId(Guid.NewGuid());

            var help = new EpisodicMemory(
                Guid.NewGuid(),
                new WDateTime(1),
                "Relation:MicroPositive|what=help-with-task|from=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                0.60,
                EmotionalTag.Positive,
                0.50,
                OtherPerson: other);

            var support = new EpisodicMemory(
                Guid.NewGuid(),
                new WDateTime(2),
                "Relation:MicroPositive|what=support-after-stress|from=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                0.60,
                EmotionalTag.Positive,
                0.50,
                OtherPerson: other);

            // Act
            var keyHelp = MemoryReinforcementKeyBuilder.From(help);
            var keySupport = MemoryReinforcementKeyBuilder.From(support);

            // Assert
            Assert.AreNotEqual(keyHelp, keySupport);
        }

        #endregion

        #region Parser

        [TestMethod]
        public void ParseDescriptor_InteractionAcceptedWithWarmth_ParsesCorrectly()
        {
            // Arrange
            const string what =
                "Interaction:Validation:Accepted|from=11111111-1111-1111-1111-111111111111|to=22222222-2222-2222-2222-222222222222|repair=true";
            const string perceivedWhat =
                "PerceivedWarmth:Interaction:Validation:Accepted";

            // Act
            var descriptor = MemoryWhatParser.ParseDescriptor(what, perceivedWhat);

            // Assert
            Assert.AreEqual("Interaction", descriptor.Category);
            Assert.AreEqual("Validation", descriptor.Type);
            Assert.AreEqual("Accepted", descriptor.Outcome);
            Assert.AreEqual(PerceivedMemoryTone.Warm, descriptor.PerceivedTone);
            Assert.AreEqual("true", descriptor.Parameters["repair"]);
        }

        [TestMethod]
        public void ParseDescriptor_Threat_IsMappedToThreatTone()
        {
            // Arrange
            const string what =
                "Interaction:Invite:Rejected|from=11111111-1111-1111-1111-111111111111|to=22222222-2222-2222-2222-222222222222";
            const string perceivedWhat =
                "PerceivedThreat:Interaction:Invite:Rejected";

            // Act
            var descriptor = MemoryWhatParser.ParseDescriptor(what, perceivedWhat);

            // Assert
            Assert.AreEqual("Interaction", descriptor.Category);
            Assert.AreEqual("Invite", descriptor.Type);
            Assert.AreEqual("Rejected", descriptor.Outcome);
            Assert.AreEqual(PerceivedMemoryTone.Threat, descriptor.PerceivedTone);
        }

        #endregion

        [TestMethod]
        public void GetMicroEventKind_Help_ReturnsCanonicalToken()
        {
            // Arrange
            const string what = "Relation:MicroPositive|what=help|from=11111111-1111-1111-1111-111111111111";
            var descriptor = MemoryWhatParser.ParseDescriptor(what, null);

            // Act
            var microKind = MemoryWhatParser.GetMicroEventKind(descriptor);

            // Assert
            Assert.AreEqual(MemoryMicroEventKinds.Help, microKind);
        }

        [TestMethod]
        public void GetMicroEventKind_HelpWithTask_IsNotCanonicalHelpToken()
        {
            // Arrange
            const string what = "Relation:MicroPositive|what=help-with-task|from=11111111-1111-1111-1111-111111111111";
            var descriptor = MemoryWhatParser.ParseDescriptor(what, null);

            // Act
            var microKind = MemoryWhatParser.GetMicroEventKind(descriptor);

            // Assert
            Assert.AreEqual(MemoryMicroEventKinds.Help, microKind);
        }

        [TestMethod]
        public void GetMicroEventKind_IgnoreAfterReachout_NormalizesToCanonicalIgnore()
        {
            // Arrange
            const string what = "Relation:MicroNegative|what=ignore-after-reachout|from=11111111-1111-1111-1111-111111111111";
            var descriptor = MemoryWhatParser.ParseDescriptor(what, null);

            // Act
            var microKind = MemoryWhatParser.GetMicroEventKind(descriptor);

            // Assert
            Assert.AreEqual(MemoryMicroEventKinds.Ignore, microKind);
        }
    }
}