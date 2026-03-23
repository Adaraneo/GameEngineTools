// AttractionCalculatorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for <see cref="DefaultAttractionCalculator"/>.
    /// Each test covers a single component or boundary condition.
    /// </summary>
    /// <remarks>
    /// Calibration table — expected component ranges for reference:
    ///
    /// | Component       | Min  | Max  |
    /// |-----------------|------|------|
    /// | BasePhysical    |  0   |  40  |
    /// | PreferenceMatch |  0   |  35  |
    /// | StateModifier   | -15  | +10  |
    /// | MereExposure    |  0   |  15  |
    /// | Score (total)   |  0   | 100  |
    /// </remarks>
    [TestClass]
    public class AttractionCalculatorTests
    {
        // ── Shared test fixtures ─────────────────────────────────────────────────

        private DefaultAttractionCalculator _sut = null!;
        private static readonly WDateTime Now = new WDateTime(0);

        [TestInitialize]
        public void Setup()
        {
            _sut = new DefaultAttractionCalculator();
        }

        // ── Score boundaries ─────────────────────────────────────────────────────

        #region Score — hranice

        /// <summary>
        /// Score must always be in [0, 100] regardless of input extremes.
        /// </summary>
        [TestMethod]
        public void Calculate_ExtremeFavourableInputs_ScoreDoesNotExceed100()
        {
            // Arrange
            var profile    = BuildNeutralProfile();
            var appearance = BuildAppearance(heightCm: 170, frame: BodyFrame.Medium);
            var view       = BuildView(postureScore: 100, acneLevel: 0, bloating: BloatingLevel.None);

            // Act
            var result = _sut.Calculate(profile, appearance, view, SexBiology.Female, positiveInteractionCount: 100);

            // Assert
            Assert.IsTrue(result.Score <= 100.0, $"Score exceeded 100: {result.Score}");
        }

        /// <summary>
        /// Score must be >= 0 even when all state modifiers are maximally negative.
        /// </summary>
        [TestMethod]
        public void Calculate_ExtremeUnfavourableState_ScoreIsNotNegative()
        {
            // Arrange
            var profile    = BuildNeutralProfile();
            var appearance = BuildAppearance(heightCm: 170, frame: BodyFrame.Medium);
            var view       = BuildView(postureScore: 0, acneLevel: 100, bloating: BloatingLevel.High);

            // Act
            var result = _sut.Calculate(profile, appearance, view, SexBiology.Female, positiveInteractionCount: 0);

            // Assert
            Assert.IsTrue(result.Score >= 0.0, $"Score was negative: {result.Score}");
        }

        #endregion

        // ── BasePhysical ─────────────────────────────────────────────────────────

        #region BasePhysical

        /// <summary>
        /// A target whose height is far outside the population window should yield
        /// a lower BasePhysical than a target at the optimum height.
        /// </summary>
        [TestMethod]
        public void Calculate_TargetAtOptimumHeight_BasePhysicalHigherThanExtremeHeight()
        {
            // Arrange
            var profile       = BuildNeutralProfile();
            var viewNeutral   = BuildView(50, 10, BloatingLevel.None);
            var appearanceOpt = BuildAppearance(heightCm: 170, frame: BodyFrame.Medium);
            var appearanceTall = BuildAppearance(heightCm: 210, frame: BodyFrame.Medium);

            // Act
            var resultOpt  = _sut.Calculate(profile, appearanceOpt,  viewNeutral, SexBiology.Female);
            var resultTall = _sut.Calculate(profile, appearanceTall, viewNeutral, SexBiology.Female);

            // Assert
            Assert.IsTrue(
                resultOpt.BasePhysical > resultTall.BasePhysical,
                $"Optimum height base={resultOpt.BasePhysical:F2} should exceed extreme height base={resultTall.BasePhysical:F2}");
        }

        /// <summary>
        /// BasePhysical must stay within its documented ceiling of 40.
        /// </summary>
        [TestMethod]
        public void Calculate_AnyInput_BasePhysicalDoesNotExceedCeiling()
        {
            // Arrange
            var profile    = BuildNeutralProfile();
            var appearance = BuildAppearance(heightCm: 168, frame: BodyFrame.Medium,
                                             noseProminence: 0.5, lipFullness: 0.5);
            var view       = BuildView(50, 0, BloatingLevel.None);

            // Act
            var result = _sut.Calculate(profile, appearance, view, SexBiology.Female);

            // Assert
            Assert.IsTrue(result.BasePhysical <= 40.0,
                $"BasePhysical {result.BasePhysical:F2} exceeded ceiling of 40.");
        }

        #endregion

        // ── PreferenceMatch ──────────────────────────────────────────────────────

        #region PreferenceMatch

        /// <summary>
        /// When the target's height exactly matches the observer's preferred height,
        /// the height contribution to PreferenceMatch should be at its maximum (15).
        /// </summary>
        [TestMethod]
        public void Calculate_TargetHeightMatchesPreference_PreferenceMatchHigherThanMismatch()
        {
            // Arrange — observer prefers 170 cm
            var profile = new AttractionProfile(
                PreferredHeightCm:  170.0,
                HeightToleranceCm:  10.0,
                FramePreference:    BodyFramePreference.None,
                PreferredWhr:       0.70,
                SymmetryWeight:     0.5,
                MereExposureWeight: 0.5);

            var viewNeutral    = BuildView(50, 10, BloatingLevel.None);
            var appearanceGood = BuildAppearance(heightCm: 170, frame: BodyFrame.Medium);
            var appearanceBad  = BuildAppearance(heightCm: 200, frame: BodyFrame.Medium);

            // Act
            var resultGood = _sut.Calculate(profile, appearanceGood, viewNeutral, SexBiology.Female);
            var resultBad  = _sut.Calculate(profile, appearanceBad,  viewNeutral, SexBiology.Female);

            // Assert
            Assert.IsTrue(
                resultGood.PreferenceMatch > resultBad.PreferenceMatch,
                $"Match at preferred height ({resultGood.PreferenceMatch:F2}) should exceed " +
                $"match at non-preferred height ({resultBad.PreferenceMatch:F2}).");
        }

        /// <summary>
        /// When the observer has a specific frame preference that matches the target,
        /// PreferenceMatch should be higher than when the frame is mismatched.
        /// </summary>
        [TestMethod]
        public void Calculate_FramePreferenceMatches_PreferenceMatchHigherThanMismatch()
        {
            // Arrange — observer prefers Petite
            var profile = new AttractionProfile(
                PreferredHeightCm:  165.0,
                HeightToleranceCm:  15.0,
                FramePreference:    BodyFramePreference.Petite,
                PreferredWhr:       0.70,
                SymmetryWeight:     0.5,
                MereExposureWeight: 0.5);

            var view            = BuildView(50, 10, BloatingLevel.None);
            var appearancePetite = BuildAppearance(heightCm: 165, frame: BodyFrame.Petite);
            var appearanceLarge  = BuildAppearance(heightCm: 165, frame: BodyFrame.Large);

            // Act
            var resultMatch    = _sut.Calculate(profile, appearancePetite, view, SexBiology.Female);
            var resultMismatch = _sut.Calculate(profile, appearanceLarge,  view, SexBiology.Female);

            // Assert
            Assert.IsTrue(
                resultMatch.PreferenceMatch > resultMismatch.PreferenceMatch,
                $"Frame match ({resultMatch.PreferenceMatch:F2}) should exceed " +
                $"frame mismatch ({resultMismatch.PreferenceMatch:F2}).");
        }

        #endregion

        // ── StateModifier ────────────────────────────────────────────────────────

        #region StateModifier

        /// <summary>
        /// High bloating (High) must produce a lower StateModifier than no bloating.
        /// </summary>
        [TestMethod]
        public void Calculate_HighBloating_StateModifierLowerThanNoBloating()
        {
            // Arrange
            var profile    = BuildNeutralProfile();
            var appearance = BuildAppearance(170, BodyFrame.Medium);
            var viewNormal = BuildView(postureScore: 70, acneLevel: 5, bloating: BloatingLevel.None);
            var viewBloat  = BuildView(postureScore: 70, acneLevel: 5, bloating: BloatingLevel.High);

            // Act
            var resultNormal = _sut.Calculate(profile, appearance, viewNormal, SexBiology.Female);
            var resultBloat  = _sut.Calculate(profile, appearance, viewBloat,  SexBiology.Female);

            // Assert
            Assert.IsTrue(
                resultNormal.StateModifier > resultBloat.StateModifier,
                $"No-bloat modifier ({resultNormal.StateModifier:F2}) should exceed " +
                $"high-bloat modifier ({resultBloat.StateModifier:F2}).");
        }

        /// <summary>
        /// Good posture (PostureScore = 100) should yield a higher StateModifier than poor posture (0).
        /// </summary>
        [TestMethod]
        public void Calculate_GoodPostureVsPoorPosture_StateModifierDiffers()
        {
            // Arrange
            var profile     = BuildNeutralProfile();
            var appearance  = BuildAppearance(170, BodyFrame.Medium);
            var viewGood    = BuildView(postureScore: 100, acneLevel: 0, bloating: BloatingLevel.None);
            var viewPoor    = BuildView(postureScore: 0,   acneLevel: 0, bloating: BloatingLevel.None);

            // Act
            var good = _sut.Calculate(profile, appearance, viewGood, SexBiology.Female);
            var poor = _sut.Calculate(profile, appearance, viewPoor, SexBiology.Female);

            // Assert
            Assert.IsTrue(
                good.StateModifier > poor.StateModifier,
                $"Good posture modifier ({good.StateModifier:F2}) must exceed " +
                $"poor posture modifier ({poor.StateModifier:F2}).");
        }

        #endregion

        // ── MereExposure ─────────────────────────────────────────────────────────

        #region MereExposure

        /// <summary>
        /// Zero interactions must yield a MereExposure of exactly 0.
        /// </summary>
        [TestMethod]
        public void Calculate_ZeroInteractions_MereExposureIsZero()
        {
            // Arrange
            var profile    = BuildNeutralProfile();
            var appearance = BuildAppearance(170, BodyFrame.Medium);
            var view       = BuildView(50, 10, BloatingLevel.None);

            // Act
            var result = _sut.Calculate(profile, appearance, view, SexBiology.Female,
                                        positiveInteractionCount: 0);

            // Assert
            Assert.AreEqual(0.0, result.MereExposure, delta: 0.001,
                "MereExposure must be zero when no positive interactions have occurred.");
        }

        /// <summary>
        /// More interactions must yield a higher MereExposure than fewer interactions.
        /// </summary>
        [TestMethod]
        public void Calculate_MoreInteractions_MereExposureIncreases()
        {
            // Arrange
            var profile    = new AttractionProfile(170, 12, BodyFramePreference.None, 0.70, 0.5, 1.0);
            var appearance = BuildAppearance(170, BodyFrame.Medium);
            var view       = BuildView(50, 10, BloatingLevel.None);

            // Act
            var resultFew  = _sut.Calculate(profile, appearance, view, SexBiology.Female,
                                            positiveInteractionCount: 2);
            var resultMany = _sut.Calculate(profile, appearance, view, SexBiology.Female,
                                            positiveInteractionCount: 20);

            // Assert
            Assert.IsTrue(
                resultMany.MereExposure > resultFew.MereExposure,
                $"More interactions ({resultMany.MereExposure:F2}) must yield higher " +
                $"mere-exposure than fewer ({resultFew.MereExposure:F2}).");
        }

        /// <summary>
        /// MereExposure must never exceed its ceiling of 15.
        /// </summary>
        [TestMethod]
        public void Calculate_ManyInteractions_MereExposureDoesNotExceedCeiling()
        {
            // Arrange — MereExposureWeight = 1.0 (max possible weight)
            var profile    = new AttractionProfile(170, 12, BodyFramePreference.None, 0.70, 0.5, 1.0);
            var appearance = BuildAppearance(170, BodyFrame.Medium);
            var view       = BuildView(50, 10, BloatingLevel.None);

            // Act
            var result = _sut.Calculate(profile, appearance, view, SexBiology.Female,
                                        positiveInteractionCount: 9999);

            // Assert
            Assert.IsTrue(result.MereExposure <= 15.0,
                $"MereExposure {result.MereExposure:F2} exceeded ceiling of 15.");
        }

        #endregion

        // ── Builders ─────────────────────────────────────────────────────────────

        #region Helpers

        /// <summary>Neutral profile with no strong preferences — mid values throughout.</summary>
        private static AttractionProfile BuildNeutralProfile()
            => new(
                PreferredHeightCm:  170.0,
                HeightToleranceCm:  15.0,
                FramePreference:    BodyFramePreference.None,
                PreferredWhr:       0.70,
                SymmetryWeight:     0.5,
                MereExposureWeight: 0.5);

        private static PhysicalAppearance BuildAppearance(
            double heightCm,
            BodyFrame frame,
            double noseProminence = 0.5,
            double lipFullness    = 0.5)
            => new(
                HeightCm:          heightCm,
                Frame:             frame,
                SkinTone:          SkinTone.Medium,
                EyeColor:          EyeColor.Brown,
                HairColor:         HairColorNatural.Brown,
                HairType:          HairType.Straight,
                FaceShape:         FaceShape.Oval,
                ShoulderBreadthCm: 40.0,
                HipBreadthCm:      38.0,
                NoseProminence:    noseProminence,
                LipFullness:       lipFullness);

        private static AppearanceView BuildView(
            double postureScore,
            double acneLevel,
            BloatingLevel bloating)
            => new(
                WeightKg:      65.0,
                Bmi:           22.0,
                BodyFatPct:    20.0,
                HairLengthCm:  25.0,
                PostureScore:  postureScore,
                SkinOiliness:  20.0,
                AcneLevel:     acneLevel,
                Bloating:      bloating);

        #endregion
    }
}
