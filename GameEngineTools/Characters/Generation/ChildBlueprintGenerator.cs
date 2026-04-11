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
            var parentHeightMean = (parentA.HeightCm + parentB.HeightCm) * 0.5;
            var heightAdjustment = Math.Clamp((parentHeightMean - 170.0) * 0.06, -5.0, 5.0);

            return baseline with
            {
                HeightCm = Round1(Math.Clamp(baseline.HeightCm + heightAdjustment + Normalish(rng) * 1.5, 45.0, 95.0)),
                Frame = PickInherited(parentA.Frame, parentB.Frame, baseline.Frame, rng),
                SkinTone = PickInherited(parentA.SkinTone, parentB.SkinTone, baseline.SkinTone, rng),
                EyeColor = PickInherited(parentA.EyeColor, parentB.EyeColor, baseline.EyeColor, rng),
                HairColor = PickInherited(parentA.HairColor, parentB.HairColor, baseline.HairColor, rng),
                HairType = PickInherited(parentA.HairType, parentB.HairType, baseline.HairType, rng),
                FaceShape = PickInherited(parentA.FaceShape, parentB.FaceShape, baseline.FaceShape, rng),
                NoseProminence = Clamp01(Blend(parentA.NoseProminence, parentB.NoseProminence, baseline.NoseProminence, rng, parentWeight: 0.35)),
                LipFullness = Clamp01(Blend(parentA.LipFullness, parentB.LipFullness, baseline.LipFullness, rng, parentWeight: 0.45))
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
