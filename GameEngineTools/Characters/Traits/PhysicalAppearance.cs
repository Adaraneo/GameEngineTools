// PhysicalAppearance.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    using System.Text.Json.Serialization;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;

    // --- Trait: stabilní rysy (genetika/morfologie) ---
    public sealed record PhysicalAppearance(
        double HeightCm,
        BodyFrame Frame,
        SkinTone SkinTone,
        EyeColor EyeColor,
        HairColorNatural HairColor,
        HairType HairType,
        FaceShape FaceShape,
        double ShoulderBreadthCm,
        double HipBreadthCm,
        double NoseProminence,   // 0..1
        double LipFullness,      // 0..1
        IReadOnlyList<string>? DistinctiveMarks = null,
        BodyMorphology? BodyMorphology = null,
        FacialMorphology? FacialMorphology = null,
        SurfaceTraits? SurfaceTraits = null,
        ColorTraits? ColorTraits = null)
    {
        /// <summary>Structured body morphology. Legacy records receive a conservative projection.</summary>
        [JsonIgnore]
        public BodyMorphology Body => BodyMorphology ?? BodyMorphology.FromLegacy(HeightCm, ShoulderBreadthCm, HipBreadthCm, Frame);

        /// <summary>Structured facial morphology. Legacy records receive a conservative projection.</summary>
        [JsonIgnore]
        public FacialMorphology Face => FacialMorphology ?? FacialMorphology.FromLegacy(FaceShape, NoseProminence, LipFullness);

        /// <summary>Structured surface traits. Legacy records receive neutral surface values.</summary>
        [JsonIgnore]
        public SurfaceTraits Surface => SurfaceTraits ?? SurfaceTraits.Neutral;

        /// <summary>Structured colour traits projected from legacy colour labels.</summary>
        [JsonIgnore]
        public ColorTraits Colors => ColorTraits ?? new(SkinTone, EyeColor, HairColor, HairType);
    }

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
            var baselineBmi = BaselineBmiFor(trait.Frame, biology);
            var bmiJitter = (50 - physio.Energy) * 0.03; // nízká energie → mírně horší BMI proxy
            var bmi = Math.Clamp(baselineBmi + bmiJitter / 10.0, 16.0, 30.0);
            var weight = bmi * Math.Pow(trait.HeightCm / 100.0, 2);

            var bodyFat = Math.Clamp(BodyFatFor(bmi, biology), 8, 45);

            // Vzhled pleti (velmi hrubě): žlázy + zánět ↔ ImmuneLoad, hormonální vlivy z cyklu přes SymptomBloat.
            var oil = Math.Clamp(20 + physio.ImmuneLoad * 0.6, 0, 100);
            var acne = Math.Clamp(10 + physio.ImmuneLoad * 0.7 + physio.BodyTempDelta * 5, 0, 100);

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
            var posture = Math.Clamp(80 - physio.SleepDebtHours * 5 - physio.Pain * 0.4, 0, 100);

            // Délka vlasů: dlouhodobá metrika – tady jen placeholder (ponecháme na tobě, nebo ulož zvlášť)
            var hairLen = 35.0; // cm – můžeš řídit zvláštní evidencí, nebo přidat do Persistence.

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

        private static double BaselineBmiFor(BodyFrame frame, SexBiology biology)
        {
            var mid = biology == SexBiology.Female ? 22.0 : 23.0;
            return frame switch
            {
                BodyFrame.Petite => mid - 1.5,
                BodyFrame.Large => mid + 1.5,
                _ => mid
            };
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
