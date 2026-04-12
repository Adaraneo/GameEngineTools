// PhysicalAppearance.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;

    // --- Trait: stabilní rysy (genetika/morfologie) ---
    public sealed record PhysicalAppearance(
        BodyMorphology Body,
        FacialMorphology Face,
        SurfaceTraits Surface,
        ColorTraits Colors,
        IReadOnlyList<string>? DistinctiveMarks = null,
        double HairLengthCm = 35.0)
    { }

    public enum BodyFrame
    { Petite, Medium, Large, Strong }

    public enum SkinTone
    {
        VeryFair, Fair, Light, LightMedium, Medium, Tan, Dark, VeryDark,
        Olive
    }

    public enum EyeColor
    { Brown, Hazel, Green, Blue, Gray, Amber }

    public enum HairColorNatural
    { Black, DarkBrown, Brown, Auburn, Red, Blond, DarkBlond }

    public enum HairType
    { Straight, Wavy, Curly, Coily }

    public enum FaceShape
    { Oval, Round, Square, Heart, Diamond, Oblong }

    // --- Projekce: stavový „aktuální vzhled“ (odvozený) ---
    public sealed record AppearanceView(
        double WeightKg,
        double Bmi,
        double BodyFatPct,
        double HairLengthCm,
        double PostureScore,       // 0..100
        double SkinOiliness,       // 0..100
        double AcneLevel,          // 0..100
        BloatingLevel Bloating,    // None/Light/Medium/High
        string? ClothingStyle = null,
        string? MakeupStyle = null);

    public enum BloatingLevel
    { None, Light, Medium, High }

    // --- Projekce: čistá funkce z trait + fyzia ---
    public static class AppearanceProjector
    {
        /// <summary>
        /// Vypočti aktuální vzhled z traitu a snapshotu fyziologie.
        /// Neprovádí side-effecty; můžeš ukládat výsledný <see cref="AppearanceView"/>.
        /// </summary>
        public static AppearanceView Compute(PhysicalAppearance trait, PhysiologyState physio, SexBiology biology)
        {
            // Hmotnost je mimo sim – očekává se, že si ji buď držíš extra, nebo aproximujeme ze 2 indexů:
            // Energy (dlouhodobě) a ImmuneLoad (krátkodobě zhorší vzhled pleti).
            // Tady volíme rozumné defaulty; můžeš je nahradit vlastní evidencí hmotnosti.
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

            // Vzhled pleti (velmi hrubě): žlázy + zánět ↔ ImmuneLoad, hormonální vlivy z cyklu přes SymptomBloat.
            var oil = Math.Clamp(18 + physio.ImmuneLoad * 0.55 + (1.0 - trait.Surface.SkinThickness) * 12.0, 0, 100);
            var acne = Math.Clamp(
                8 + physio.ImmuneLoad * 0.65 + physio.BodyTempDelta * 5 + (1.0 - trait.Surface.SkinSmoothness) * 18.0,
                0,
                100);

            // Nadmutí (bloat) – PMS/menses/luteální fáze zvyšují retenci vody
            var bloat = BloatingLevel.None;
            if (physio.Cycle is { } c)
            {
                bloat = c.Phase switch
                {
                    CyclePhase.Menses => BloatingLevel.Medium,
                    CyclePhase.Luteal => BloatingLevel.Light,
                    _ => BloatingLevel.None
                };
                // Pokud máš v State už symptom bloat 0..100, přemapuj:
                if (c.SymptomBloat >= 66)
                {
                    bloat = BloatingLevel.High;
                }
                else if (c.SymptomBloat >= 33)
                {
                    bloat = BloatingLevel.Medium;
                }
            }

            // Držení těla a kvalita „vzhledu“ klesá s únavou/bolestí
            var posture = Math.Clamp(
                trait.Body.Posture.PostureUprightness * 100.0 - physio.SleepDebtHours * 5 - physio.Pain * 0.4,
                0,
                100);

            var hairLen = Math.Clamp(trait.HairLengthCm, 0.0, 120.0);

            return new AppearanceView(
                WeightKg: Round1(weight),
                Bmi: Math.Round(bmi, 1),
                BodyFatPct: Math.Round(bodyFat, 1),
                HairLengthCm: Round1(hairLen),
                PostureScore: Math.Round(posture, 1),
                SkinOiliness: Math.Round(oil, 1),
                AcneLevel: Math.Round(acne, 1),
                Bloating: bloat
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
            // Deurenbergův odhad BF% ~ BMI + 0.23*age - 5.4 - 10.8*sex (sex=1 muži) – věk nemáme, použijeme baseline.
            var sexAdj = (biology == SexBiology.Male) ? -10.8 : 0.0;
            var baseEst = bmi - 5.4 + sexAdj; // zjednodušení bez věku
            return baseEst;
        }

        private static double Round1(double v) => Math.Round(v, 1);
    }
}
