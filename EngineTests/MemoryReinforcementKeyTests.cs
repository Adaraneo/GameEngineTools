// MemoryReinforcementKeyTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;

    [TestClass]
    public class MemoryReinforcementKeyTests
    {
        [TestMethod]
        public void From_InteractionSameTypeAndPerson_ProducesSameKey()
        {
            // Arrange
            var other = new HumanId(Guid.NewGuid());

            var a = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(1),
                "Interaction:Invite:Accepted|from=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|to=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                0.7,
                EmotionalTag.Positive,
                0.5,
                OtherPerson: other);

            var b = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(2),
                "Interaction:Invite:Accepted|from=cccccccccccccccccccccccccccccccc|to=dddddddddddddddddddddddddddddddd",
                0.7,
                EmotionalTag.Positive,
                0.5,
                OtherPerson: other);

            // Act
            var keyA = MemoryReinforcementKeyBuilder.From(a);
            var keyB = MemoryReinforcementKeyBuilder.From(b);

            // Assert
            Assert.AreEqual(keyA, keyB);
        }

        [TestMethod]
        public void From_MicroPositiveDifferentMeaning_ProducesDifferentKey()
        {
            // Arrange
            var other = new HumanId(Guid.NewGuid());

            var help = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(1),
                "Relation:MicroPositive|from=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|what=help-with-task",
                0.6,
                EmotionalTag.Positive,
                0.5,
                OtherPerson: other);

            var support = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(2),
                "Relation:MicroPositive|from=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|what=support-after-stress",
                0.6,
                EmotionalTag.Positive,
                0.5,
                OtherPerson: other);

            // Act
            var keyHelp = MemoryReinforcementKeyBuilder.From(help);
            var keySupport = MemoryReinforcementKeyBuilder.From(support);

            // Assert
            Assert.AreNotEqual(keyHelp, keySupport);
        }

        [TestMethod]
        public void From_SleepEndedDifferentHoursSameQuality_ProducesSameKey()
        {
            // Arrange
            var a = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(1),
                "Sleep:Ended:High|hours=7.5",
                0.5,
                EmotionalTag.Positive,
                0.5);

            var b = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(2),
                "Sleep:Ended:High|hours=8.2",
                0.5,
                EmotionalTag.Positive,
                0.5);

            // Act
            var keyA = MemoryReinforcementKeyBuilder.From(a);
            var keyB = MemoryReinforcementKeyBuilder.From(b);

            // Assert
            Assert.AreEqual(keyA, keyB);
        }

        [TestMethod]
        public void From_InteractionSameTypeButDifferentPerson_ProducesDifferentKey()
        {
            // Arrange
            var otherA = new HumanId(Guid.NewGuid());
            var otherB = new HumanId(Guid.NewGuid());

            var a = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(1),
                "Interaction:Invite:Accepted|from=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|to=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                0.7,
                EmotionalTag.Positive,
                0.5,
                OtherPerson: otherA);

            var b = new EpisodicMemory(
                Guid.NewGuid(),
                new GameEngineTools.World.Utils.Time.WDateTime(2),
                "Interaction:Invite:Accepted|from=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|to=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                0.7,
                EmotionalTag.Positive,
                0.5,
                OtherPerson: otherB);

            // Act
            var keyA = MemoryReinforcementKeyBuilder.From(a);
            var keyB = MemoryReinforcementKeyBuilder.From(b);

            // Assert
            Assert.AreNotEqual(keyA, keyB);
        }
    }
}