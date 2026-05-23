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
    /// Blends parent genetic latents with a baby-stage baseline and deterministic variation.
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

            var baselineBlueprint = _appearanceGenerator.GenerateBlueprint(biology, runtimeSeed);
            var parentABlueprint = parentA.GeneticBlueprint ?? baselineBlueprint;
            var parentBBlueprint = parentB.GeneticBlueprint ?? baselineBlueprint;
            var inheritedBlueprint = InheritLatents(parentABlueprint, parentBBlueprint, baselineBlueprint, rng);

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
                inheritedBlueprint,
                AttractionProfile: null,
                Seed: runtimeSeed);
        }

        #endregion IChildBlueprintGenerator

        #region Inheritance

        private static GeneticBlueprint InheritLatents(
            GeneticBlueprint parentA,
            GeneticBlueprint parentB,
            GeneticBlueprint baseline,
            IRandomSource rng)
        {
            // Height: blend parent HeightNorm values with mild regression toward mean
            var parentHeightNorm = (parentA.BodyLatent.HeightNorm + parentB.BodyLatent.HeightNorm) * 0.5;
            var heightNorm = ClampSigned(parentHeightNorm * 0.45 + baseline.BodyLatent.HeightNorm * 0.55 + Normalish(rng) * 0.10);

            // Nose and lip signals from face latents
            var nose = Clamp01(Blend(parentA.FaceLatent.NoseScale, parentB.FaceLatent.NoseScale, baseline.FaceLatent.NoseScale, rng, parentWeight: 0.35));
            var lip = Clamp01(Blend(parentA.FaceLatent.LipFullness, parentB.FaceLatent.LipFullness, baseline.FaceLatent.LipFullness, rng, parentWeight: 0.45));

            // Colors — direct genetic inheritance
            var colors = new ColorTraits(
                PickInherited(parentA.Colors.SkinTone, parentB.Colors.SkinTone, baseline.Colors.SkinTone, rng),
                PickInherited(parentA.Colors.EyeColor, parentB.Colors.EyeColor, baseline.Colors.EyeColor, rng),
                PickInherited(parentA.Colors.HairColor, parentB.Colors.HairColor, baseline.Colors.HairColor, rng),
                PickInherited(parentA.Colors.HairType, parentB.Colors.HairType, baseline.Colors.HairType, rng));

            return baseline with
            {
                Colors = colors,
                BodyLatent = baseline.BodyLatent with { HeightNorm = Round3(heightNorm) },
                FaceLatent = baseline.FaceLatent with { NoseScale = Round3(nose), LipFullness = Round3(lip) }
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
            if (r < 0.42) return parentA;
            if (r < 0.84) return parentB;
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

        private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

        private static double ClampSigned(double value) => Math.Clamp(value, -1.0, 1.0);

        private static double Round3(double value) => Math.Round(value, 3);

        private static int DeriveSeed(HumanId parentA, HumanId parentB, WDateOnly bornOn)
        {
            unchecked
            {
                var hash = 17;
                foreach (var b in parentA.Value.ToByteArray())
                    hash = hash * 31 + b;
                foreach (var b in parentB.Value.ToByteArray())
                    hash = hash * 31 + b;
                hash = hash * 31 + bornOn.DayIndex.GetHashCode();
                return hash;
            }
        }

        #endregion Helpers
    }
}
