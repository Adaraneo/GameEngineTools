using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Generation;
using GameEngineTools.World.Utils.Time;

namespace EngineTests
{
    internal sealed class FixedIndexRandom : IRandomSource
    {
        private readonly int _fixedIndex;

        public FixedIndexRandom(int fixedIndex = 0) => _fixedIndex = fixedIndex;

        public bool Chance(double p) => false;

        public int Next(int minInclusive, int maxExclusive) => _fixedIndex;

        public double NextUnit() => 0.0;
    }

    internal static class TestData
    {
        public static Name MakeName(string original) => new Name { Original = original, Familiar = [] };

        public static Surname MakeSurname(string male, string female) => new Surname { Male = male, Female = female };

        public static WDateOnly SomeBirthDate => new WDateOnly(10000);
    }

    [TestClass]
    public class SimpleIdentityGeneratorTests
    {
        private Name[] _femaleNames = null;
        private Name[] _maleNames = null;
        private Surname[] _surnames = null;
        private SimpleIdentityGenerator _sut = null!;

        [TestInitialize]
        public void Setup()
        {
            _femaleNames =
            [
                TestData.MakeName("Anna"),
                TestData.MakeName("Barbora"),
            ];
            _maleNames =
            [
                TestData.MakeName("Petr"),
                TestData.MakeName("Jan"),
            ];
            _surnames =
            [
                TestData.MakeSurname("Novák", "Nováková"),
            ];

            _sut = new SimpleIdentityGenerator(_femaleNames, _maleNames, _surnames);
        }

        [TestMethod]
        public void Generate_FemaleSex_ReturnsNameFromFemaleList()
        {
            var rng = new FixedIndexRandom();
            var identity = _sut.Generate(SexBiology.Female, TestData.SomeBirthDate, rng);

            Assert.AreEqual("Anna", identity.FirstName.Original);
        }

        [TestMethod]
        public void Generate_MaleSex_ReturnsNameFromMaleList()
        {
            var rng = new FixedIndexRandom();
            var identity = _sut.Generate(SexBiology.Male, TestData.SomeBirthDate, rng);

            Assert.AreEqual("Petr", identity.FirstName.Original);
        }

        //[TestMethod]
        //TODO: Check!
        public void Generate_FemaleSex_WithSecondIndex_ReturnsSecondFemaleNames()
        {
            var rng = new FixedIndexRandom(1);

            var identity = _sut.Generate(SexBiology.Female, TestData.SomeBirthDate, rng);

            Assert.AreEqual("Barbora", identity.FirstName.Original);
        }

        [TestMethod]
        public void Generate_AnyInput_BirthDatePreservedInIdentity()
        {
            var expectedDate = new WDateOnly(99_999);
            var rng = new FixedIndexRandom(0);

            var identity = _sut.Generate(SexBiology.Female, expectedDate, rng);

            Assert.AreEqual(expectedDate, identity.BirthDate);
        }

        [TestMethod]
        public void Generate_AnyInput_ReturnsSurnameFromList()
        {
            var rng = new FixedIndexRandom(0);

            var identity = _sut.Generate(SexBiology.Female, TestData.SomeBirthDate, rng);

            Assert.AreSame(_surnames[0], identity.LastName);
        }

        /// <summary>
        /// Dokumentační test: aktuální implementace pro Intersex/Unknown
        /// používá mužský seznam jmen (else větev).
        /// Pokud toto chování změníš, test tě upozorní.
        /// </summary>
        [DataTestMethod]
        [DataRow(SexBiology.Intersex)]
        [DataRow(SexBiology.Unknown)]
        public void Generate_IntersexOrUnknown_CurrentlyUseMaleNames(SexBiology sex)
        {
            var rng = new FixedIndexRandom(0);

            var identity = _sut.Generate(sex, TestData.SomeBirthDate, rng);

            Assert.AreEqual("Petr", identity.FirstName.Original, $"Pro pohlaví {sex} se aktuálně používá mužský seznam – zkontroluj záměr.");
        }

        [TestMethod]
        public void Generate_EmptyFemaleNames_ThrowsInvalidOperationException()
        {
            var sut = new SimpleIdentityGenerator(
                femaleNames: [],
                maleNames: _maleNames,
                surnames: _surnames);
            var rng = new FixedIndexRandom(0);

            Assert.Throws<InvalidOperationException>(
                () => sut.Generate(SexBiology.Female, TestData.SomeBirthDate, rng));
        }

        [TestMethod]
        public void Generate_EmptyMaleNames_ThrowsInvalidOperationException()
        {
            var sut = new SimpleIdentityGenerator(
                femaleNames: _femaleNames,
                maleNames: [],
                surnames: _surnames);
            var rng = new FixedIndexRandom(0);

            Assert.Throws<InvalidOperationException>(
                () => sut.Generate(SexBiology.Male, TestData.SomeBirthDate, rng));
        }

        [TestMethod]
        public void Generate_EmptySurnames_ThrowsInvalidOperationException()
        {
            var sut = new SimpleIdentityGenerator(
                femaleNames: _femaleNames,
                maleNames: _maleNames,
                surnames: []);
            var rng = new FixedIndexRandom(0);

            Assert.Throws<InvalidOperationException>(
                () => sut.Generate(SexBiology.Female, TestData.SomeBirthDate, rng));
        }

        [TestMethod]
        public void Generate_ValidInput_ReturnsNonNullIdentity()
        {
            var rng = new FixedIndexRandom(0);

            var identity = _sut.Generate(SexBiology.Male, TestData.SomeBirthDate, rng);

            Assert.IsNotNull(identity);
            Assert.IsNotNull(identity.FirstName);
            Assert.IsNotNull(identity.LastName);
        }
    }
}
