// SeededPersonTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>A seeded person is the same person every time; an unseeded one is new.</summary>
    [TestClass]
    public sealed class SeededPersonTests : TestBase
    {
        [TestMethod]
        public void RandomizePerson_WithOneSeed_GivesTheSameIdentityAndTraits()
        {
            var a = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 18, seed: 1234);
            var b = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 18, seed: 1234);

            Assert.AreEqual(a.Id, b.Id);
            Assert.AreEqual(a.Identity, b.Identity);
            Assert.AreEqual(a.Biology, b.Biology);
            Assert.AreEqual(a.Personality, b.Personality);
        }

        [TestMethod]
        public void RandomizePerson_WithDifferentSeeds_GivesDifferentPeople()
        {
            var a = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 18, seed: 1);
            var b = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 18, seed: 2);

            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreNotEqual(a.Personality, b.Personality);
        }

        [TestMethod]
        public void RandomizePerson_WithoutASeed_GivesANewPersonEachTime()
        {
            var a = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 18);
            var b = CharacterManager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 18);

            Assert.AreNotEqual(a.Id, b.Id);
        }
    }
}
