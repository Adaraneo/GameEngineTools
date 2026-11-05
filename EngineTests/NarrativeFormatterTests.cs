// NarrativeFormatterTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Bereavement;
    using GameEngineTools.Characters.Engines.Economy;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Narrative;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;

    /// <summary>Czech narration of the life, death, body, economy and norm events.</summary>
    [TestClass]
    public sealed class NarrativeFormatterTests
    {
        private static readonly HumanId Anna = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        private static readonly HumanId Petr = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));
        private static readonly WDateTime Now = new(1000);

        private static readonly DefaultNarrativeFormatter Formatter = new();

        private static NarrativeCharacterInfo Resolve(HumanId id)
            => id == Anna ? new NarrativeCharacterInfo("Anna", SexBiology.Female) : new NarrativeCharacterInfo("Petr", SexBiology.Male);

        private static NarrativeEntry? Format(IDomainEvent ev) => Formatter.Format(ev, Resolve);

        [TestMethod]
        public void Death_IsNarratedWithItsCause_AndHighPriority()
        {
            var entry = Format(new CharacterDied(Now, Petr, DeathCause.OldAge));

            Assert.IsNotNull(entry);
            Assert.AreEqual("Petr zemřel — stářím.", entry!.Text);
            Assert.AreEqual(NarrativePriority.High, entry.Priority);
            Assert.AreEqual(Petr, entry.Subject);
        }

        [TestMethod]
        public void Death_UsesTheFeminineForm_ForAWoman()
        {
            Assert.AreEqual("Anna zemřela — hladem.", Format(new CharacterDied(Now, Anna, DeathCause.Starvation))!.Text);
        }

        [TestMethod]
        public void Bereavement_Funeral_Burial_AndGraveVisit_AreNarrated()
        {
            var bereavement = Format(new BereavementOnset(Now, Anna, Petr, 80, DeathCause.OldAge, GameEngineTools.Characters.Engines.Relationships.KinRole.Partner));
            var funeral = Format(new FuneralHeld(Now, Anna, Petr, Attendees: 4));
            var buried = Format(new Buried(Now, Anna, Petr));
            var visit = Format(new GraveVisited(Now, Anna, Petr));

            StringAssert.Contains(bereavement!.Text, "Anna oplakává ztrátu");
            StringAssert.Contains(funeral!.Text, "pohřbu");
            StringAssert.Contains(funeral.Text, "přítomných: 4");
            StringAssert.Contains(buried!.Text, "byl pohřben");
            StringAssert.Contains(visit!.Text, "navštívila hrob");
        }

        [TestMethod]
        public void Birth_Pregnancy_AndInjury_AreNarrated()
        {
            StringAssert.Contains(Format(new ChildBorn(Now, Anna, Petr))!.Text, "narodilo se dítě");
            StringAssert.Contains(Format(new PregnancyDiscovered(Now, Anna, Petr))!.Text, "je těhotná");
            Assert.AreEqual(NarrativePriority.High, Format(new InjuryReceived(Now, Petr, 8.0, InjuryType.Wound))!.Priority);
            Assert.AreEqual(NarrativePriority.Medium, Format(new InjuryReceived(Now, Petr, 2.0, InjuryType.Sprain))!.Priority);
            StringAssert.Contains(Format(new InjuryHealed(Now, Petr))!.Text, "se uzdravil");
        }

        [TestMethod]
        public void PurchaseSaleAndNormViolation_AreNarrated()
        {
            StringAssert.Contains(Format(new Purchased(Now, Petr, "o1", PickupItemKind.Bread, "bakery", 2.0, 8.0))!.Text, "koupil chléb za");
            StringAssert.Contains(Format(new Sold(Now, Anna, "o1", PickupItemKind.Cheese, "bakery", 1.2, 9.0))!.Text, "prodala sýr");
            StringAssert.Contains(Format(new NormViolationOccurred(Now, Petr, GameEngineTools.Characters.Engines.Interactions.SocialNormKind.RitualContext, 0.8, HasAudience: true))!.Text, "před lidmi");
        }

        [TestMethod]
        public void EventsWithoutAStory_StillProduceNoEntry()
        {
            Assert.IsNull(Format(new WageEarned(Now, Anna, "farmer", 1.0, 1.0, 9.0)), "Wages would flood the feed.");
        }
    }
}
