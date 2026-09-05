// DialogueContractsTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Dialogue.Contracts;
    using GameEngineTools.Dialogue.Semantics;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Immutable;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Phase-1 serialization/identity guarantees for the dialogue contracts: a <see cref="SpeechAct"/>
    /// must survive a System.Text.Json round trip (including its <c>ImmutableDictionary</c> of roles,
    /// <see cref="EntityRef"/>s and <see cref="ForceShift"/>), and <see cref="EntityId"/> must faithfully
    /// carry a <see cref="HumanId"/> both in memory and through JSON.
    /// </summary>
    [TestClass]
    public class DialogueContractsTests : TestBase
    {
        private static JsonSerializerOptions BuildOptions() => new()
        {
            Converters =
            {
                new WDateTimeJsonConverter(),
                new HumanIdJsonConverter(),
                new JsonStringEnumConverter()
            }
        };

        [TestMethod]
        public void SpeechActRoundTrip_WithRolesForceShiftAndObjectRole_PreservesAllFields()
        {
            var speaker = new HumanId(Guid.NewGuid());
            var addressee = new HumanId(Guid.NewGuid());
            var now = WDateTime.New(WDateOnly.New(100, 1, 1));

            var original = SpeechAct.Relational(RelationalActKind.Invite, speaker, addressee, now, "Petr", "Jana") with
            {
                PredicateLemma = "pozvat",
                Polarity = Polarity.Negative,
                Register = Register.Formal,
                Directness = Directness.Indirect,
                Dimensions = DialogueDimension.SocialObligation | DialogueDimension.TurnManagement,
                ForceShift = new ForceShift(IllocutionaryPoint.Assertive, Polarity.Affirmative),
                Roles = ImmutableDictionary<FgdFunctor, EntityRef>.Empty
                    .Add(FgdFunctor.ACT, EntityRef.ForHuman(speaker, "Petr"))
                    .Add(FgdFunctor.ADDR, EntityRef.ForHuman(addressee, "Jana"))
                    .Add(FgdFunctor.PAT, new EntityRef(EntityId.ForObject("tavern_01"), "hospoda"))
            };

            var options = BuildOptions();
            var json = JsonSerializer.Serialize(original, options);
            var restored = JsonSerializer.Deserialize<SpeechAct>(json, options)!;

            // Scalar / structural fields (records + record structs compare by value).
            Assert.AreEqual(original.Point, restored.Point);
            Assert.AreEqual(original.RelationalKind, restored.RelationalKind);
            Assert.AreEqual(original.Dimensions, restored.Dimensions);
            Assert.AreEqual(original.PredicateLemma, restored.PredicateLemma);
            Assert.AreEqual(original.Polarity, restored.Polarity);
            Assert.AreEqual(original.Register, restored.Register);
            Assert.AreEqual(original.Directness, restored.Directness);
            Assert.AreEqual(original.ForceShift, restored.ForceShift);
            Assert.AreEqual(original.Speaker, restored.Speaker);
            Assert.AreEqual(original.Addressee, restored.Addressee);
            Assert.AreEqual(original.OccurredAt, restored.OccurredAt);

            // ImmutableDictionary has no structural equality — compare by content.
            Assert.AreEqual(original.Roles.Count, restored.Roles.Count);
            foreach (var (functor, reference) in original.Roles)
            {
                Assert.IsTrue(restored.Roles.TryGetValue(functor, out var restoredRef), $"Missing role {functor}.");
                Assert.AreEqual(reference, restoredRef);
            }
        }

        [TestMethod]
        public void FgdFunctorKeys_SerializeAsNames_NotOrdinals()
        {
            var speaker = new HumanId(Guid.NewGuid());
            var addressee = new HumanId(Guid.NewGuid());
            var act = SpeechAct.Relational(RelationalActKind.SmallTalk, speaker, addressee, WDateTime.New(WDateOnly.New(100, 1, 1)));

            var json = JsonSerializer.Serialize(act, BuildOptions());

            StringAssert.Contains(json, "\"ACT\"");
            StringAssert.Contains(json, "\"ADDR\"");
        }

        [TestMethod]
        public void EntityIdOfHuman_TryAsHumanId_RecoversOriginalGuid()
        {
            var human = new HumanId(Guid.NewGuid());

            var id = EntityId.Of(human);

            Assert.AreEqual(EntityKind.Human, id.Kind);
            Assert.IsTrue(id.TryAsHumanId(out var recovered));
            Assert.AreEqual(human, recovered);
        }

        [TestMethod]
        public void EntityIdOfObject_TryAsHumanId_ReturnsFalse()
        {
            var id = EntityId.ForObject("fireplace_01");

            Assert.AreEqual(EntityKind.Object, id.Kind);
            Assert.IsFalse(id.TryAsHumanId(out _));
        }

        [TestMethod]
        public void EntityIdRoundTrip_ThroughJson_PreservesKindAndValue()
        {
            var human = new HumanId(Guid.NewGuid());
            var original = EntityId.Of(human);
            var options = BuildOptions();

            var json = JsonSerializer.Serialize(original, options);
            var restored = JsonSerializer.Deserialize<EntityId>(json, options);

            Assert.AreEqual(original, restored);
            Assert.IsTrue(restored.TryAsHumanId(out var recovered));
            Assert.AreEqual(human, recovered);
        }

        [TestMethod]
        public void LemmaAffectRecordRoundTrip_ThroughJson_PreservesFields()
        {
            var original = new LemmaAffectRecord(-0.35, 0.85, AffectSource.Curated, PowerAgent: 0.8, AgencyAgent: -0.5);

            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<LemmaAffectRecord>(json);

            Assert.AreEqual(original, restored);
        }

        [TestMethod]
        public void LemmaAffectRecord_PowerAgency_DefaultToZero()
        {
            var record = new LemmaAffectRecord(0.5, 0.0, AffectSource.Curated);
            Assert.AreEqual(0.0, record.PowerAgent);
            Assert.AreEqual(0.0, record.AgencyAgent);
        }

        [TestMethod]
        public void ConnotationLexicon_UnknownLemma_FallsBackToNeutral()
        {
            var lexicon = new CuratedConnotationLexicon();

            var record = lexicon.Lookup("neexistující_lemma");

            Assert.AreEqual(new LemmaAffectRecord(0.0, 0.0, AffectSource.Curated), record);
        }
    }
}
