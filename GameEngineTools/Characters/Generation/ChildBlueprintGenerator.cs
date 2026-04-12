// ChildBlueprintGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Creates a newborn character blueprint from two parent characters.
    /// This is a generation service, not a physiology concern.
    /// </summary>
    public interface IChildBlueprintGenerator
    {
        /// <summary>Creates a child blueprint born at <paramref name="bornOn"/>.</summary>
        HumanBlueprint Generate(IHuman parentA, IHuman parentB, WDateOnly bornOn, int? seed = null);
    }

    /// <summary>
    /// Minimal inheritance-based implementation for newborn blueprints.
    /// It blends parent traits with a baby-stage baseline and deterministic variation.
    /// </summary>
    public sealed class ChildBlueprintGenerator : IChildBlueprintGenerator
    {
        #region Fields

        private readonly IRandomSourceFactory _rngFactory;
        private readonly IIdentityGenerator _identityGenerator;
        private readonly IPersonalityGenerator _personalityGenerator;
        private readonly IAppearanceGenerator _appearanceGenerator;

        #endregion Fields

        #region Constructor

        /// <summary>Initializes all dependencies through DI.</summary>
        public ChildBlueprintGenerator(
            IRandomSourceFactory rngFactory,
            IIdentityGenerator identityGenerator,
            IPersonalityGenerator personalityGenerator,
            IAppearanceGenerator appearanceGenerator)
        {
            _rngFactory = rngFactory;
            _identityGenerator = identityGenerator;
            _personalityGenerator = personalityGenerator;
            _appearanceGenerator = appearanceGenerator;
        }

        #endregion Constructor

        #region IChildBlueprintGenerator

        /// <inheritdoc/>
        public HumanBlueprint Generate(IHuman parentA, IHuman parentB, WDateOnly bornOn, int? seed = null)
        {
            ArgumentNullException.ThrowIfNull(parentA);
            ArgumentNullException.ThrowIfNull(parentB);

            var runtimeSeed = seed ?? DeriveSeed(parentA.Id, parentB.Id, bornOn);
            var rng = _rngFactory.Create(runtimeSeed);

            var biology = PickBiology(rng);
            var identity = CreateIdentity(parentA, biology, bornOn, rng);
            var baselineAppearance = _appearanceGenerator.Generate(biology, runtimeSeed, StadiumType.Baby);
            var appearance = InheritAppearance(parentA.PhysicalAppearance, parentB.PhysicalAppearance, baselineAppearance, rng);
            var baselinePersonality = _personalityGenerator.Generate(
                runtimeSeed,
                PersonalityHints.ForStadium(StadiumType.Baby),
                PersonalitySpec.ForStadium(StadiumType.Baby));
            var personality = InheritTemperament(parentA.Personality, parentB.Personality, baselinePersonality, rng);

            return new HumanBlueprint(
                new HumanId(Guid.NewGuid()),
                identity,
                biology,
                personality,
                appearance,
                AttractionProfile: null,
                Seed: runtimeSeed);
        }

        #endregion IChildBlueprintGenerator

        #region Inheritance

        private static PhysicalAppearance InheritAppearance(
            PhysicalAppearance parentA,
            PhysicalAppearance parentB,
            PhysicalAppearance baseline,
            IRandomSource rng)
        {
            var parentHeightMean = (parentA.Body.Proportions.HeightCm + parentB.Body.Proportions.HeightCm) * 0.5;
            var heightAdjustment = Math.Clamp((parentHeightMean - 170.0) * 0.06, -5.0, 5.0);
            var height = Round1(Math.Clamp(baseline.Body.Proportions.HeightCm + heightAdjustment + Normalish(rng) * 1.5, 45.0, 95.0));
            var noseProjection = Clamp01(Blend(parentA.Face.Nose.NoseProjection, parentB.Face.Nose.NoseProjection, baseline.Face.Nose.NoseProjection, rng, parentWeight: 0.35));
            var lipFullness = Clamp01(Blend(
                (parentA.Face.Mouth.UpperLipFullness + parentA.Face.Mouth.LowerLipFullness) * 0.5,
                (parentB.Face.Mouth.UpperLipFullness + parentB.Face.Mouth.LowerLipFullness) * 0.5,
                (baseline.Face.Mouth.UpperLipFullness + baseline.Face.Mouth.LowerLipFullness) * 0.5,
                rng,
                parentWeight: 0.45));

            return baseline with
            {
                Body = ScaleBodyToHeight(baseline.Body, height),
                Face = ApplyInheritedFaceSignals(baseline.Face, noseProjection, lipFullness),
                Colors = new ColorTraits(
                    PickInherited(parentA.Colors.SkinTone, parentB.Colors.SkinTone, baseline.Colors.SkinTone, rng),
                    PickInherited(parentA.Colors.EyeColor, parentB.Colors.EyeColor, baseline.Colors.EyeColor, rng),
                    PickInherited(parentA.Colors.HairColor, parentB.Colors.HairColor, baseline.Colors.HairColor, rng),
                    PickInherited(parentA.Colors.HairType, parentB.Colors.HairType, baseline.Colors.HairType, rng)),
                HairLengthCm = Round1(Math.Clamp(baseline.HairLengthCm + Normalish(rng) * 1.5, 0.0, 12.0))
            };
        }

        private static Personality InheritTemperament(
            Personality parentA,
            Personality parentB,
            Personality baseline,
            IRandomSource rng)
        {
            var inheritedBigFive = new BigFive(
                Blend(parentA.BigFive.Openness, parentB.BigFive.Openness, baseline.BigFive.Openness, rng, parentWeight: 0.35),
                Blend(parentA.BigFive.Conscientiousness, parentB.BigFive.Conscientiousness, baseline.BigFive.Conscientiousness, rng, parentWeight: 0.25),
                Blend(parentA.BigFive.Extraversion, parentB.BigFive.Extraversion, baseline.BigFive.Extraversion, rng, parentWeight: 0.35),
                Blend(parentA.BigFive.Agreeableness, parentB.BigFive.Agreeableness, baseline.BigFive.Agreeableness, rng, parentWeight: 0.30),
                Blend(parentA.BigFive.Neuroticism, parentB.BigFive.Neuroticism, baseline.BigFive.Neuroticism, rng, parentWeight: 0.30));

            return baseline with
            {
                BigFive = inheritedBigFive,
                Sociosexuality = Sociosexuality.Restricted,
                Motivation = baseline.Motivation with { Sexuality = 0.0 }
            };
        }

        #endregion Inheritance

        #region Helpers

        private Identity CreateIdentity(IHuman parentA, SexBiology biology, WDateOnly bornOn, IRandomSource rng)
        {
            var generated = _identityGenerator.Generate(biology, bornOn, rng);
            return generated with { LastName = parentA.Identity.LastName };
        }

        private static SexBiology PickBiology(IRandomSource rng)
            => rng.NextUnit() < 0.5 ? SexBiology.Female : SexBiology.Male;

        private static T PickInherited<T>(T parentA, T parentB, T baseline, IRandomSource rng)
        {
            var r = rng.NextUnit();
            if (r < 0.42)
            {
                return parentA;
            }

            if (r < 0.84)
            {
                return parentB;
            }

            return baseline;
        }

        private static double Blend(double parentA, double parentB, double baseline, IRandomSource rng, double parentWeight)
        {
            var parentMean = (parentA + parentB) * 0.5;
            var jitter = Normalish(rng) * 0.06;
            return Clamp01(parentMean * parentWeight + baseline * (1.0 - parentWeight) + jitter);
        }

        private static double Normalish(IRandomSource rng)
            => rng.NextUnit() + rng.NextUnit() + rng.NextUnit() - 1.5;

        private static double Clamp01(double value)
            => Math.Clamp(value, 0.0, 1.0);

        private static double Round1(double value)
            => Math.Round(value, 1);

        private static BodyMorphology ScaleBodyToHeight(BodyMorphology body, double heightCm)
        {
            var scale = heightCm / Math.Max(1.0, body.Proportions.HeightCm);
            var proportions = body.Proportions with
            {
                HeightCm = heightCm,
                SittingHeight = Round1(body.Proportions.SittingHeight * scale),
                LegLength = Round1(body.Proportions.LegLength * scale),
                TorsoLength = Round1(body.Proportions.TorsoLength * scale),
                ArmLength = Round1(body.Proportions.ArmLength * scale),
                ForearmLength = Round1(body.Proportions.ForearmLength * scale),
                UpperArmLength = Round1(body.Proportions.UpperArmLength * scale),
                NeckLength = Round1(body.Proportions.NeckLength * scale)
            };

            return body with
            {
                Proportions = proportions,
                Skeletal = body.Skeletal with
                {
                    ClavicleBreadth = Round1(body.Skeletal.ClavicleBreadth * scale),
                    ShoulderBreadth = Round1(body.Skeletal.ShoulderBreadth * scale),
                    RibcageWidth = Round1(body.Skeletal.RibcageWidth * scale),
                    RibcageDepth = Round1(body.Skeletal.RibcageDepth * scale),
                    ChestBreadth = Round1(body.Skeletal.ChestBreadth * scale),
                    PelvicBreadth = Round1(body.Skeletal.PelvicBreadth * scale),
                    WaistBaseWidth = Round1(body.Skeletal.WaistBaseWidth * scale),
                    NeckThickness = Round1(body.Skeletal.NeckThickness * scale),
                    HandSize = Round1(body.Skeletal.HandSize * scale),
                    FootSize = Round1(body.Skeletal.FootSize * scale)
                },
                Silhouette = body.Silhouette with
                {
                    WaistWidth = Round1(body.Silhouette.WaistWidth * scale),
                    HipWidth = Round1(body.Silhouette.HipWidth * scale)
                }
            };
        }

        private static FacialMorphology ApplyInheritedFaceSignals(FacialMorphology face, double noseProjection, double lipFullness)
        {
            return face with
            {
                Nose = face.Nose with
                {
                    NoseProjection = noseProjection,
                    NoseTipProjection = Clamp01((face.Nose.NoseTipProjection + noseProjection) * 0.5)
                },
                Mouth = face.Mouth with
                {
                    UpperLipFullness = Clamp01(lipFullness * 0.92),
                    LowerLipFullness = lipFullness,
                    VermilionHeight = Clamp01(0.22 + lipFullness * 0.31)
                }
            };
        }

        private static int DeriveSeed(HumanId parentA, HumanId parentB, WDateOnly bornOn)
        {
            unchecked
            {
                var hash = 17;
                foreach (var b in parentA.Value.ToByteArray())
                {
                    hash = hash * 31 + b;
                }

                foreach (var b in parentB.Value.ToByteArray())
                {
                    hash = hash * 31 + b;
                }

                hash = hash * 31 + bornOn.DayIndex.GetHashCode();
                return hash;
            }
        }

        #endregion Helpers
    }
}
