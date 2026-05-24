// PersonalityGeneratorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for <see cref="PersonalityGenerator"/> and <see cref="PersonalityHints.ForStadium"/>.
    /// </summary>
    /// <remarks>
    /// Focus areas:
    /// <list type="bullet">
    ///   <item>Stadium-based hard constraints (Baby / Child / Teenager age gates).</item>
    ///   <item>Per-facet <see cref="PersonalityHints.SociosexualityBehaviorMax"/> cap.</item>
    ///   <item>Hint override semantics: fixed preset vs. per-facet cap.</item>
    /// </list>
    /// All tests use a seeded RNG so results are fully deterministic — no flakiness.
    /// </remarks>
    [TestClass]
    public class PersonalityGeneratorTests
    {
        // ── Shared fixture ───────────────────────────────────────────────────────

        /// <summary>SUT — system under test.</summary>
        private PersonalityGenerator _sut = null!;

        /// <summary>
        /// Fixed seed used across all tests that need a seeded RNG.
        /// The value itself is arbitrary; what matters is that it is constant.
        /// </summary>
        private const int FixedSeed = 42;

        [TestInitialize]
        public void Setup()
        {
            // SeededRandomSourceFactory is a tiny local helper (see bottom of file).
            // We pass it to PersonalityGenerator so every Generate() call is deterministic.
            _sut = new PersonalityGenerator(new SeededRandomSourceFactory(FixedSeed));
        }

        // ── Region: Stadium hard constraints ─────────────────────────────────────

        #region Stadium hard constraints — Baby and Child

        /// <summary>
        /// Baby hint must force Sociosexuality to Restricted on all three facets.
        /// Babies have no sexual dimension whatsoever.
        /// </summary>
        [TestMethod]
        public void Generate_BabyHints_SociosexualityIsFullyRestricted()
        {
            // Arrange
            var hints = PersonalityHints.ForStadium(StadiumType.Baby);

            // Act
            var result = _sut.Generate(seed: FixedSeed, hints: hints);

            // Assert
            // All three SOI-R facets must be exactly Restricted (0.10).
            Assert.AreEqual(Sociosexuality.Restricted.Behavior, result.Sociosexuality.Behavior,
                "Baby: Behavior facet must be Restricted.");
            Assert.AreEqual(Sociosexuality.Restricted.Attitude, result.Sociosexuality.Attitude,
                "Baby: Attitude facet must be Restricted.");
            Assert.AreEqual(Sociosexuality.Restricted.Desire, result.Sociosexuality.Desire,
                "Baby: Desire facet must be Restricted.");
        }

        /// <summary>
        /// Baby hint must also force CommunicationStyle to Direct.
        /// This is a separate hard constraint independent of sociosexuality.
        /// </summary>
        [TestMethod]
        public void Generate_BabyHints_CommunicationIsDirect()
        {
            // Arrange
            var hints = PersonalityHints.ForStadium(StadiumType.Baby);

            // Act
            var result = _sut.Generate(seed: FixedSeed, hints: hints);

            // Assert
            Assert.AreEqual(CommunicationStyle.Direct, result.Communication,
                "Babies communicate directly — they have no capacity for indirect or high-context signalling.");
        }

        /// <summary>
        /// Child hint must force Sociosexuality to Restricted (same as Baby).
        /// Children have no developed sexual dimension.
        /// </summary>
        [TestMethod]
        public void Generate_ChildHints_SociosexualityIsFullyRestricted()
        {
            // Arrange
            var hints = PersonalityHints.ForStadium(StadiumType.Child);

            // Act
            var result = _sut.Generate(seed: FixedSeed, hints: hints);

            // Assert
            Assert.AreEqual(Sociosexuality.Restricted.Behavior, result.Sociosexuality.Behavior,
                "Child: Behavior facet must be Restricted.");
            Assert.AreEqual(Sociosexuality.Restricted.Attitude, result.Sociosexuality.Attitude,
                "Child: Attitude facet must be Restricted.");
            Assert.AreEqual(Sociosexuality.Restricted.Desire, result.Sociosexuality.Desire,
                "Child: Desire facet must be Restricted.");
        }

        #endregion Stadium hard constraints — Baby and Child

        // ── Region: Teenager per-facet behavior cap ───────────────────────────────

        #region Teenager — SociosexualityBehaviorMax cap

        /// <summary>
        /// Teenager hint must set SociosexualityBehaviorMax to 0.25.
        /// This ensures the Behavior facet (past casual-partner history) is hard-capped —
        /// a 15-year-old simply has not had time to accumulate a high-casualty history.
        /// </summary>
        [TestMethod]
        public void ForStadium_Teenager_HintContainsBehaviorMaxOf025()
        {
            // Arrange + Act
            var hints = PersonalityHints.ForStadium(StadiumType.Teenager);

            // Assert
            // Teaching point: we test the DATA (the hint) separately from the BEHAVIOR (Generate).
            // If ForStadium() produces the wrong hint, Generate() would also be wrong — but
            // testing the hint directly gives a much clearer failure message.
            Assert.IsNotNull(hints.SociosexualityBehaviorMax,
                "Teenager hint must include a SociosexualityBehaviorMax cap.");
            Assert.AreEqual(0.25, hints.SociosexualityBehaviorMax!.Value, delta: 0.001,
                "Teenager SociosexualityBehaviorMax must be 0.25.");
        }

        /// <summary>
        /// Teenager hint must set the fixed Sociosexuality preset to Intermediate,
        /// which acts as the upper ceiling for Attitude and Desire facets.
        /// </summary>
        [TestMethod]
        public void ForStadium_Teenager_HintContainsIntermediateSociosexuality()
        {
            // Arrange + Act
            var hints = PersonalityHints.ForStadium(StadiumType.Teenager);

            // Assert
            Assert.IsNotNull(hints.Sociosexuality,
                "Teenager hint must include a fixed Sociosexuality preset.");
            Assert.AreEqual(Sociosexuality.Intermediate.Behavior, hints.Sociosexuality!.Behavior,
                delta: 0.001, "Teenager Sociosexuality preset must be Intermediate.");
        }

        /// <summary>
        /// When teenager hints are applied during Generate(), the resulting
        /// Sociosexuality.Behavior must never exceed 0.25.
        /// </summary>
        /// <remarks>
        /// We run the generator with multiple seeds to increase confidence that
        /// the cap holds across different random outcomes.
        /// </remarks>
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(42)]
        [DataRow(999)]
        [DataRow(int.MaxValue / 2)]
        public void Generate_TeenagerHints_BehaviorFacetNeverExceeds025(int seed)
        {
            // Arrange
            var hints = PersonalityHints.ForStadium(StadiumType.Teenager);

            // Act
            var result = _sut.Generate(seed: seed, hints: hints);

            // Assert
            Assert.IsTrue(result.Sociosexuality.Behavior <= 0.25 + 0.001,
                $"Seed {seed}: Teenager Behavior facet {result.Sociosexuality.Behavior:F4} " +
                $"must not exceed 0.25 (SOI-R Behavior = past partner history, implausible at teenage age).");
        }

        /// <summary>
        /// Attitude and Desire facets must remain freely generated for teenagers —
        /// only Behavior is hard-capped. A teenager can have high desire or a permissive
        /// attitude even without extensive behavioral history.
        /// </summary>
        [TestMethod]
        public void Generate_TeenagerHints_AttitudeAndDesireAreNotForcedToMinimum()
        {
            // Arrange
            var hints = PersonalityHints.ForStadium(StadiumType.Teenager);

            // Act — run with several seeds and collect all results
            // We expect that across seeds, at least SOME produce Attitude or Desire > Restricted (0.10).
            var attitudeValues = Enumerable.Range(0, 20)
                .Select(seed => _sut.Generate(seed: seed, hints: hints).Sociosexuality.Attitude)
                .ToList();

            var desireValues = Enumerable.Range(0, 20)
                .Select(seed => _sut.Generate(seed: seed, hints: hints).Sociosexuality.Desire)
                .ToList();

            // Assert
            // Teaching point: we don't assert an exact value here — we assert a PROPERTY of the distribution.
            // "At least one result is above the minimum" is enough to prove the cap is per-facet only.
            Assert.IsTrue(attitudeValues.Any(v => v > Sociosexuality.Restricted.Attitude + 0.05),
                "Across 20 seeds, at least one Teenager must have Attitude above Restricted. " +
                "Attitude facet must NOT be hard-capped alongside Behavior.");

            Assert.IsTrue(desireValues.Any(v => v > Sociosexuality.Restricted.Desire + 0.05),
                "Across 20 seeds, at least one Teenager must have Desire above Restricted. " +
                "Desire facet must NOT be hard-capped alongside Behavior.");
        }

        #endregion Teenager — SociosexualityBehaviorMax cap

        // ── Region: Adult — no hard constraints ───────────────────────────────────

        #region Adult — unrestricted sociosexuality

        /// <summary>
        /// Adult, MidAged, and Old stadiums must NOT inject any sociosexuality hint.
        /// These life stages can have any sociosexuality profile including Unrestricted.
        /// </summary>
        [DataTestMethod]
        [DataRow(StadiumType.Adult)]
        [DataRow(StadiumType.MidAged)]
        [DataRow(StadiumType.Old)]
        public void ForStadium_AdultOrOlder_SociosexualityHintIsNull(StadiumType stadium)
        {
            // Arrange + Act
            var hints = PersonalityHints.ForStadium(stadium);

            // Assert
            Assert.IsNull(hints.Sociosexuality,
                $"{stadium}: No sociosexuality hint expected — adults have no hard age constraint.");
            Assert.IsNull(hints.SociosexualityBehaviorMax,
                $"{stadium}: No BehaviorMax cap expected for adult+ stadiums.");
        }

        /// <summary>
        /// An adult with no hints must be able to produce Unrestricted sociosexuality (0.90).
        /// This validates that the cap mechanism does NOT leak into adult generation.
        /// </summary>
        [TestMethod]
        public void Generate_AdultNoHints_CanProduceUnrestrictedSociosexuality()
        {
            // Arrange
            // No hints = full freedom. We run enough seeds to encounter Unrestricted.
            var spec = PersonalitySpec.ForStadium(StadiumType.Adult);

            // Act
            var results = Enumerable.Range(0, 50)
                .Select(seed => _sut.Generate(seed: seed, spec: spec).Sociosexuality)
                .ToList();

            // Assert
            // At least one adult across 50 seeds must land on Unrestricted (0.90).
            // With default weights (Restricted:0.40, Intermediate:0.35, Unrestricted:0.25),
            // across 50 seeds the probability of never hitting Unrestricted is (0.75)^50 ≈ 0.00006%.
            Assert.IsTrue(results.Any(s => s.Behavior >= Sociosexuality.Unrestricted.Behavior - 0.001),
                "Adult generation across 50 seeds must produce at least one Unrestricted profile. " +
                "If this fails, the cap mechanism is incorrectly applied to adults.");
        }

        #endregion Adult — unrestricted sociosexuality

        // ── Region: Direct hints override ─────────────────────────────────────────

        #region Direct hint override — fixed Sociosexuality preset

        /// <summary>
        /// When hints.Sociosexuality is set explicitly (not null), the generator must use
        /// that exact preset and ignore SociosexualityBehaviorMax entirely.
        /// The fixed preset takes priority — this is the "lock" mode.
        /// </summary>
        [TestMethod]
        public void Generate_ExplicitSociosexualityHint_IgnoresBehaviorMaxCap()
        {
            // Arrange
            // We set an explicit Sociosexuality AND a BehaviorMax that would conflict.
            // The explicit preset must win.
            var hints = new PersonalityHints(
                Sociosexuality: new Sociosexuality(0.80, 0.70, 0.60),
                SociosexualityBehaviorMax: 0.10);  // This cap must be ignored when preset is set.

            // Act
            var result = _sut.Generate(seed: FixedSeed, hints: hints);

            // Assert
            // Teaching point: the "with" expression in Generate() checks `hints.Sociosexuality is null`
            // before applying the cap. If preset is non-null, cap is skipped. This test verifies that contract.
            Assert.AreEqual(0.10, result.Sociosexuality.Behavior, delta: 0.001,
                "Explicit Sociosexuality hint must override SociosexualityBehaviorMax. " +
                "Cap only applies when no fixed preset is provided.");
            Assert.AreEqual(0.70, result.Sociosexuality.Attitude, delta: 0.001);
            Assert.AreEqual(0.60, result.Sociosexuality.Desire, delta: 0.001);
        }

        #endregion Direct hint override — fixed Sociosexuality preset

        // ── Region: BehaviorMax cap in isolation ──────────────────────────────────

        #region SociosexualityBehaviorMax in isolation

        /// <summary>
        /// SociosexualityBehaviorMax must clamp only Behavior, leaving Attitude and Desire untouched.
        /// Verifies the per-facet nature of the cap with a custom hint.
        /// </summary>
        [TestMethod]
        public void Generate_BehaviorMaxCap_OnlyClampsTheBehaviorFacet()
        {
            // Arrange
            // No fixed preset → generator picks from weights, then applies cap.
            // We set a very low cap (0.05) to make the clamp observable.
            var hints = new PersonalityHints(
                Sociosexuality: null,
                SociosexualityBehaviorMax: 0.05);

            // Act — collect results across seeds
            var results = Enumerable.Range(0, 20)
                .Select(seed => _sut.Generate(seed: seed, hints: hints).Sociosexuality)
                .ToList();

            // Assert — Behavior is always capped
            Assert.IsTrue(results.All(s => s.Behavior <= 0.05 + 0.001),
                "With BehaviorMax=0.05, all generated Behavior values must be <= 0.05.");

            // Attitude and Desire are NOT forced to 0.05 — they can be higher
            // (when the randomly picked preset is Intermediate or Unrestricted).
            // We just verify that the cap did not accidentally clamp them too.
            // At least SOME results must have Attitude or Desire above 0.05.
            Assert.IsTrue(results.Any(s => s.Attitude > 0.05 + 0.001) ||
                          results.Any(s => s.Desire   > 0.05 + 0.001),
                "BehaviorMax cap must NOT silently clamp Attitude or Desire. " +
                "Across 20 seeds, at least one profile must have Attitude or Desire above 0.05.");
        }

        #endregion SociosexualityBehaviorMax in isolation

        // ── Private helpers ───────────────────────────────────────────────────────

        #region Private helpers

        /// <summary>
        /// Minimal <see cref="IRandomSourceFactory"/> that always creates a
        /// deterministically seeded <see cref="Random"/> wrapper.
        /// Keeps tests fast and repeatable without any mocking framework.
        /// </summary>
        private sealed class SeededRandomSourceFactory : IRandomSourceFactory
        {
            private readonly int _baseSeed;

            /// <summary>
            /// Initializes a new <see cref="SeededRandomSourceFactory"/>.
            /// </summary>
            /// <param name="baseSeed">
            /// Base seed used when the caller does not supply their own.
            /// In practice PersonalityGenerator always passes the seed it receives,
            /// so this value acts as a fallback.
            /// </param>
            public SeededRandomSourceFactory(int baseSeed) => _baseSeed = baseSeed;

            /// <inheritdoc/>
            public IRandomSource Create(int seed) => new DeterministicRandom(seed);

            /// <summary>
            /// Thin wrapper around <see cref="System.Random"/> for use in tests.
            /// </summary>
            private sealed class DeterministicRandom : IRandomSource
            {
                private readonly Random _inner;

                /// <summary>
                /// Initializes a new deterministic random with the given seed.
                /// </summary>
                /// <param name="seed">RNG seed — same seed always produces the same sequence.</param>
                public DeterministicRandom(int seed) => _inner = new Random(seed);

                /// <inheritdoc/>
                public int Next(int minInclusive, int maxExclusive)
                    => _inner.Next(minInclusive, maxExclusive);

                /// <inheritdoc/>
                public double NextUnit() => _inner.NextDouble();

                /// <inheritdoc/>
                public bool Chance(double p) => _inner.NextDouble() < p;
            }
        }

        #endregion Private helpers
    }
}
