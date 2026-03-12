// AppearanceGenerator.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Hosting.Defaults;
using GameEngineTools.Characters.Traits;

namespace GameEngineTools.Characters.Generation;

/// <summary>
/// Kompletně nový, ne-lokalizovaný a vysoce realistický generátor fyzického vzhledu.
/// - Využívá korigované náhodné proměnné (výška ↔ šířky ramen/kyčlí, rám těla ↔ proporce obličeje…)
/// - Konfigurovatelné přes <see cref="AppearanceGenSpec"/> (rozsahy, váhy, korelace). Default je rozumný a neutrální.
/// - Deterministický při zadání seed.
/// </summary>
public interface IAppearanceGenerator
{
    PhysicalAppearance Generate(SexBiology sex, int seed, AppearanceGenSpec? spec = null);
}

/// <summary>
/// Parametrizace generátoru. Vše je kulturně agnostické; čísla jsou v neutrálních rozmezích.
/// </summary>
public sealed record AppearanceGenSpec(
    // Výšky v cm
    (double Min, double Max) HeightFemale,
    (double Min, double Max) HeightMale,

    // Váhy kategorií
    double[] BodyFrameWeights,      // Petite, Medium, Large, Strong
    double[] SkinToneWeights,       // Fair, Light, LightMedium, Medium, Tan
    double[] EyeColorWeights,       // Brown, Hazel, Green, Blue, Gray
    double[] HairColorWeights,      // Black, DarkBrown, Brown, DarkBlond, Blond
    double[] HairTypeWeights,       // Straight, Wavy, Curly
    double[] FaceShapeWeights,      // Oval, Round, Heart, Square, Oblong

    // Korelace a šumy (0..1). Vyšší číslo = silnější vliv.
    double HeightToShoulderCorr,
    double HeightToHipCorr,
    double FrameToBreadthsCorr,

    // Rozsahy šířek v cm (bazální před korekcemi)
    (double Min, double Max) ShoulderBreadthBase,
    (double Min, double Max) HipBreadthBaseFemale,
    (double Min, double Max) HipBreadthBaseMale,

    // Jemné morfologické detaily 0..1 (prominence/nosy, plnost rtů)
    (double Mean, double Dev) NoseProminence,
    (double Mean, double Dev) LipFullness
)
{
    public static AppearanceGenSpec Default => new(
        HeightFemale: (155, 175),
        HeightMale: (165, 185),
        BodyFrameWeights: Uniform(4),
        SkinToneWeights: Uniform(5),
        EyeColorWeights: Uniform(5),
        HairColorWeights: Uniform(5),
        HairTypeWeights: new[] { 0.42, 0.40, 0.18 },
        FaceShapeWeights: Uniform(5),
        HeightToShoulderCorr: 0.55,
        HeightToHipCorr: 0.45,
        FrameToBreadthsCorr: 0.35,
        ShoulderBreadthBase: (36, 46),
        HipBreadthBaseFemale: (36, 44),
        HipBreadthBaseMale: (34, 42),
        NoseProminence: (0.50, 0.20),
        LipFullness: (0.50, 0.20)
    );

    public static double[] Uniform(int n)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = 1.0 / n;
        }

        return a;
    }
}

public sealed class AppearanceGenerator : IAppearanceGenerator
{
    private readonly IRandomSourceFactory _rngFactory;

    public AppearanceGenerator(IRandomSourceFactory rngFactory)
        => _rngFactory = rngFactory;

    public PhysicalAppearance Generate(SexBiology sex, int seed, AppearanceGenSpec? spec = null)
    {
        spec ??= AppearanceGenSpec.Default;
        var rng = _rngFactory.Create(seed);

        // 1) Výška (hlavní latentní proměnná)
        var (hMin, hMax) = sex == SexBiology.Female ? spec.HeightFemale : spec.HeightMale;
        var height = Lerp(rng.NextUnit(), hMin, hMax);

        // 2) Rám těla (diskrétní kategorie)
        var frame = Pick(new[] { BodyFrame.Petite, BodyFrame.Medium, BodyFrame.Large, BodyFrame.Strong }, spec.BodyFrameWeights, rng);

        // 3) Bazální šířky + korelace
        var shoulderBase = Lerp(rng.NextUnit(), spec.ShoulderBreadthBase.Min, spec.ShoulderBreadthBase.Max);
        var (hipMin, hipMax) = sex == SexBiology.Female ? spec.HipBreadthBaseFemale : spec.HipBreadthBaseMale;
        var hipBase = Lerp(rng.NextUnit(), hipMin, hipMax);

        // Korelační korekce (lineární):
        var heightNorm = (height - ((hMin + hMax) * 0.5)) / ((hMax - hMin) * 0.5); // -1..1
        var frameBias = frame switch
        {
            BodyFrame.Petite => -0.35,
            BodyFrame.Medium => 0.00,
            BodyFrame.Large => 0.25,
            BodyFrame.Strong => 0.40,
            _ => 0
        };

        var shoulder = shoulderBase
                      + spec.HeightToShoulderCorr * heightNorm * 2.0
                      + spec.FrameToBreadthsCorr * frameBias * 2.0
                      + Jitter(rng, 0.6);

        var hip = hipBase
                + spec.HeightToHipCorr * heightNorm * 2.0
                + spec.FrameToBreadthsCorr * frameBias * 1.5
                + (sex == SexBiology.Female ? 0.6 : -0.4) // lehká dimorfie
                + Jitter(rng, 0.6);

        shoulder = Clamp(shoulder, spec.ShoulderBreadthBase.Min, spec.ShoulderBreadthBase.Max);
        hip = Clamp(hip, hipMin, hipMax);

        static double SoftClamp(double v, double min, double max, double k = 0.2)
        {
            if (v < min)
            {
                return min + (v - min) * k;
            }

            if (v > max)
            {
                return max + (v - max) * k;
            }

            return v;
        }

        var shr = shoulder / height; // shoulder/height
        var hhr = hip / height; // hip/height
        var (shrMin, shrMax) = (sex == SexBiology.Female ? 0.20 : 0.22,
        sex == SexBiology.Female ? 0.26 : 0.28);
        var (hhrMin, hhrMax) = (sex == SexBiology.Female ? 0.20 : 0.19,
        sex == SexBiology.Female ? 0.27 : 0.26);
        shoulder = SoftClamp(shoulder, shrMin * height, shrMax * height);
        hip = SoftClamp(hip, hhrMin * height, hhrMax * height);

        // 4) Barvy a tvary (nezávisle, ale s jemným biasem dle frame u FaceShape)
        var skin = Pick(new[] { SkinTone.Fair, SkinTone.Light, SkinTone.LightMedium, SkinTone.Medium, SkinTone.Tan }, spec.SkinToneWeights, rng);
        var eyes = Pick(new[] { EyeColor.Brown, EyeColor.Hazel, EyeColor.Green, EyeColor.Blue, EyeColor.Gray }, spec.EyeColorWeights, rng);
        var hairC = Pick(new[] { HairColorNatural.Black, HairColorNatural.DarkBrown, HairColorNatural.Brown, HairColorNatural.DarkBlond, HairColorNatural.Blond }, spec.HairColorWeights, rng);
        var hairT = Pick(new[] { HairType.Straight, HairType.Wavy, HairType.Curly }, spec.HairTypeWeights, rng);

        var faceWeights = (double[])spec.FaceShapeWeights.Clone();
        // Subtilní bias: robustnější rám posune trochu k Square/Oblong, drobnější k Oval/Heart.
        switch (frame)
        {
            case BodyFrame.Petite:
                Bias(faceWeights, FaceShape.Oval, +0.05);
                Bias(faceWeights, FaceShape.Heart, +0.03);
                Bias(faceWeights, FaceShape.Square, -0.04);
                break;

            case BodyFrame.Large:
            case BodyFrame.Strong:
                Bias(faceWeights, FaceShape.Square, +0.05);
                Bias(faceWeights, FaceShape.Oblong, +0.03);
                Bias(faceWeights, FaceShape.Heart, -0.03);
                break;
        }
        Normalize(faceWeights);
        var face = Pick(new[] { FaceShape.Oval, FaceShape.Round, FaceShape.Heart, FaceShape.Square, FaceShape.Oblong }, faceWeights, rng);

        // 5) Jemné rysy
        double N(double mean, double dev) => Clamp(mean + dev * (rng.NextUnit() + rng.NextUnit() + rng.NextUnit() - 1.5), 0.0, 1.0);
        var nose = Math.Round(N(spec.NoseProminence.Mean, spec.NoseProminence.Dev), 2);
        var lips = Math.Round(N(spec.LipFullness.Mean + (sex == SexBiology.Female ? +0.03 : -0.02), spec.LipFullness.Dev), 2);

        return new PhysicalAppearance(
            HeightCm: height,
            Frame: frame,
            SkinTone: skin,
            EyeColor: eyes,
            HairColor: hairC,
            HairType: hairT,
            FaceShape: face,
            ShoulderBreadthCm: shoulder,
            HipBreadthCm: hip,
            NoseProminence: nose,
            LipFullness: lips,
            DistinctiveMarks: null // plug-in bod: lze dodělat přes vlastní factory
        );
    }

    // ===== Helpers =====

    private static double Lerp(double u, double a, double b) => a + (b - a) * u;

    private static double Clamp(double x, double a, double b) => x < a ? a : (x > b ? b : x);

    private static double Jitter(IRandomSource rng, double amplitude) => (rng.NextUnit() - 0.5) * amplitude;

    private static T Pick<T>(IReadOnlyList<T> values, IReadOnlyList<double> weights, IRandomSource rng)
    {
        double s = 0, r = rng.NextUnit();
        for (int i = 0; i < values.Count; i++)
        {
            s += weights[i]; if (r <= s)
            {
                return values[i];
            }
        }
        return values[^1];
    }

    private static void Bias(double[] w, FaceShape target, double delta)
    {
        var idx = target switch
        {
            FaceShape.Oval => 0,
            FaceShape.Round => 1,
            FaceShape.Heart => 2,
            FaceShape.Square => 3,
            FaceShape.Oblong => 4,
            _ => 0
        };
        w[idx] = Math.Max(0.0, w[idx] + delta);
    }

    private static void Normalize(double[] w)
    {
        var sum = 0.0; foreach (var x in w)
        {
            sum += x;
        }

        if (sum <= 0) { var u = 1.0 / w.Length; for (int i = 0; i < w.Length; i++) { w[i] = u; } return; }
        for (int i = 0; i < w.Length; i++)
        {
            w[i] /= sum;
        }
    }
}
