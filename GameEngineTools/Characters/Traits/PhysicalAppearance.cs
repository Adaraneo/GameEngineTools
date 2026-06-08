// PhysicalAppearance.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;

    /// <summary>
    /// Stable physical-appearance traits (genetics/morphology). The runtime "current look" is
    /// derived from this plus physiology via <see cref="AppearanceProjector"/>.
    /// </summary>
    /// <param name="Body">Body morphology (skeleton, soft tissue, proportions, posture).</param>
    /// <param name="Face">Facial morphology.</param>
    /// <param name="Surface">Skin/hair surface traits.</param>
    /// <param name="Colors">Pigmentation traits (skin, eyes, hair).</param>
    /// <param name="DistinctiveMarks">Optional distinctive marks (scars, tattoos, …).</param>
    /// <param name="HairLengthCm">Baseline hair length in cm.</param>
    public sealed record PhysicalAppearance(
        BodyMorphology Body,
        FacialMorphology Face,
        SurfaceTraits Surface,
        ColorTraits Colors,
        IReadOnlyList<string>? DistinctiveMarks = null,
        double HairLengthCm = 35.0)
    { }

    /// <summary>Overall body frame size.</summary>
    public enum BodyFrame
    {
        /// <summary>Petite frame.</summary>
        Petite,

        /// <summary>Medium frame.</summary>
        Medium,

        /// <summary>Large frame.</summary>
        Large,

        /// <summary>Strong/robust frame.</summary>
        Strong
    }

    /// <summary>Skin tone category.</summary>
    public enum SkinTone
    {
        /// <summary>Very fair.</summary>
        VeryFair,

        /// <summary>Fair.</summary>
        Fair,

        /// <summary>Light.</summary>
        Light,

        /// <summary>Light-medium.</summary>
        LightMedium,

        /// <summary>Medium.</summary>
        Medium,

        /// <summary>Tan.</summary>
        Tan,

        /// <summary>Dark.</summary>
        Dark,

        /// <summary>Very dark.</summary>
        VeryDark,

        /// <summary>Olive.</summary>
        Olive
    }

    /// <summary>Eye colour.</summary>
    public enum EyeColor
    {
        /// <summary>Brown.</summary>
        Brown,

        /// <summary>Hazel.</summary>
        Hazel,

        /// <summary>Green.</summary>
        Green,

        /// <summary>Blue.</summary>
        Blue,

        /// <summary>Gray.</summary>
        Gray,

        /// <summary>Amber.</summary>
        Amber
    }

    /// <summary>Natural hair colour.</summary>
    public enum HairColorNatural
    {
        /// <summary>Black.</summary>
        Black,

        /// <summary>Dark brown.</summary>
        DarkBrown,

        /// <summary>Brown.</summary>
        Brown,

        /// <summary>Auburn.</summary>
        Auburn,

        /// <summary>Red.</summary>
        Red,

        /// <summary>Blond.</summary>
        Blond,

        /// <summary>Dark blond.</summary>
        DarkBlond
    }

    /// <summary>Hair texture type.</summary>
    public enum HairType
    {
        /// <summary>Straight.</summary>
        Straight,

        /// <summary>Wavy.</summary>
        Wavy,

        /// <summary>Curly.</summary>
        Curly,

        /// <summary>Coily.</summary>
        Coily
    }

    /// <summary>Face shape.</summary>
    public enum FaceShape
    {
        /// <summary>Oval.</summary>
        Oval,

        /// <summary>Round.</summary>
        Round,

        /// <summary>Square.</summary>
        Square,

        /// <summary>Heart.</summary>
        Heart,

        /// <summary>Diamond.</summary>
        Diamond,

        /// <summary>Oblong.</summary>
        Oblong
    }

    /// <summary>
    /// Derived "current look" projection — the appearance a character presents this tick,
    /// computed from stable traits and the live physiological/aging state.
    /// </summary>
    /// <param name="WeightKg">Estimated body weight in kg.</param>
    /// <param name="Bmi">Estimated body-mass index.</param>
    /// <param name="BodyFatPct">Estimated body-fat percentage.</param>
    /// <param name="HairLengthCm">Current hair length in cm.</param>
    /// <param name="PostureScore">Posture/bearing quality, 0..100.</param>
    /// <param name="SkinOiliness">Skin oiliness, 0..100.</param>
    /// <param name="AcneLevel">Acne level, 0..100.</param>
    /// <param name="Bloating">Current bloating level.</param>
    public sealed record AppearanceView(
        double WeightKg,
        double Bmi,
        double BodyFatPct,
        double HairLengthCm,
        double PostureScore,       // 0..100
        double SkinOiliness,       // 0..100
        double AcneLevel,          // 0..100
        BloatingLevel Bloating,    // None/Light/Medium/High
        /// <summary>Fraction of grey hair (0..1); from PhysicalAgingState.GreyFraction.</summary>
        double GreyFraction = 0.0,
        /// <summary>Hair density/fullness (0..1); from PhysicalAgingState.HairDensity.</summary>
        double HairDensity = 1.0,
        /// <summary>Wrinkle score (0..100); from PhysicalAgingState.WrinkleScore.</summary>
        double WrinkleScore = 0.0,
        string? ClothingStyle = null,
        string? MakeupStyle = null);

    /// <summary>Discrete water-retention / bloating level.</summary>
    public enum BloatingLevel
    {
        /// <summary>No bloating.</summary>
        None,

        /// <summary>Light bloating.</summary>
        Light,

        /// <summary>Medium bloating.</summary>
        Medium,

        /// <summary>High bloating.</summary>
        High
    }

    /// <summary>
    /// Pure projection from stable traits plus physiology to a derived <see cref="AppearanceView"/>.
    /// Stateless and side-effect-free.
    /// </summary>
    public static class AppearanceProjector
    {
        /// <summary>
        /// Computes the current appearance from the trait and the physiology snapshot.
        /// Has no side effects; you can store the resulting <see cref="AppearanceView"/>.
        /// </summary>
        public static AppearanceView Compute(
            PhysicalAppearance trait,
            PhysiologyState physio,
            SexBiology biology,
            PhysicalAgingState? aging = null)
        {
            // Weight is outside the sim – it is expected you track it separately, or we approximate it from 2 indices:
            // Energy (long-term) and ImmuneLoad (short-term, worsens skin appearance).
            // Here we pick reasonable defaults; you can replace them with your own weight tracking.
            var baselineBmi = BaselineBmiFor(trait.Body, biology);
            var morphologyBmi = 18.5 + trait.Body.SoftTissue.Adiposity * 8.0 + trait.Body.SoftTissue.Muscularity * 2.2;
            var bmiJitter = (50 - physio.Energy) * 0.003; // nízká energie → mírně horší BMI proxy
            var bmi = Math.Clamp((baselineBmi * 0.35) + (morphologyBmi * 0.65) + bmiJitter, 16.0, 30.0);
            var heightCm = trait.Body.Proportions.HeightCm;
            var weight = bmi * Math.Pow(heightCm / 100.0, 2);

            var bodyFat = Math.Clamp(
                BodyFatFor(bmi, biology) * 0.45 + (8.0 + trait.Body.SoftTissue.Adiposity * 37.0) * 0.55,
                8,
                45);

            // Skin appearance (very roughly): glands + inflammation ↔ ImmuneLoad, hormonal effects from the cycle via SymptomBloat.
            var oil = Math.Clamp(18 + physio.ImmuneLoad * 0.55 + (1.0 - trait.Surface.SkinThickness) * 12.0, 0, 100);
            var acne = Math.Clamp(
                8 + physio.ImmuneLoad * 0.65 + physio.BodyTempDelta * 5 + (1.0 - trait.Surface.SkinSmoothness) * 18.0,
                0,
                100);

            // Bloat – PMS/menses/luteal phase increases water retention
            var bloat = BloatingLevel.None;
            if (physio.Cycle is { } c)
            {
                bloat = c.Phase switch
                {
                    CyclePhase.Menses => BloatingLevel.Medium,
                    CyclePhase.Luteal => BloatingLevel.Light,
                    _ => BloatingLevel.None
                };
                // If the State already has a bloat symptom 0..100, remap it:
                if (c.SymptomBloat >= 66)
                {
                    bloat = BloatingLevel.High;
                }
                else if (c.SymptomBloat >= 33)
                {
                    bloat = BloatingLevel.Medium;
                }
            }

            // Posture and overall "look" quality decline with fatigue/pain
            var posture = Math.Clamp(
                trait.Body.Posture.PostureUprightness * 100.0 - physio.SleepDebtHours * 5 - physio.Pain * 0.4,
                0,
                100);

            // Hair: the runtime aging state overrides the static trait
            var hairLen = Math.Clamp(aging?.HairLengthCm ?? trait.HairLengthCm, 0.0, 120.0);
            var greyFrac = Math.Clamp(aging?.GreyFraction ?? 0.0, 0, 1);
            var hairDens = Math.Clamp(aging?.HairDensity ?? 1.0, 0, 1);
            var wrinkles = Math.Clamp(aging?.WrinkleScore ?? 0.0, 0, 100);

            return new AppearanceView(
                WeightKg: Round1(weight),
                Bmi: Math.Round(bmi, 1),
                BodyFatPct: Math.Round(bodyFat, 1),
                HairLengthCm: Round1(hairLen),
                PostureScore: Math.Round(posture, 1),
                SkinOiliness: Math.Round(oil, 1),
                AcneLevel: Math.Round(acne, 1),
                Bloating: bloat,
                GreyFraction: Math.Round(greyFrac, 3),
                HairDensity: Math.Round(hairDens, 3),
                WrinkleScore: Math.Round(wrinkles, 1)
            );
        }

        private static double BaselineBmiFor(BodyMorphology body, SexBiology biology)
        {
            var mid = biology == SexBiology.Female ? 22.0 : 23.0;
            var robustnessShift = (body.Skeletal.SkeletalRobustness - 0.5) * 2.2;
            var adiposityShift = (body.SoftTissue.Adiposity - 0.5) * 1.5;
            return mid + robustnessShift + adiposityShift;
        }

        private static double BodyFatFor(double bmi, SexBiology biology)
        {
            // Deurenberg's BF% estimate ~ BMI + 0.23*age - 5.4 - 10.8*sex (sex=1 male) – we lack age, so we use a baseline.
            var sexAdj = (biology == SexBiology.Male) ? -10.8 : 0.0;
            var baseEst = bmi - 5.4 + sexAdj; // zjednodušení bez věku
            return baseEst;
        }

        private static double Round1(double v) => Math.Round(v, 1);
    }
}
