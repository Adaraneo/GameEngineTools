// AppearanceMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Pure static helpers that expand latent vectors into morphology records.
    /// Called by both <see cref="AppearanceGenerator"/> and <see cref="AppearanceProjector"/>.
    /// </summary>
    internal static class AppearanceMath
    {
        #region Public API

        /// <summary>
        /// Recomputes correlated face latent from a projected body latent, then overlays
        /// the independent genetic signals from <paramref name="genetic"/>.
        /// This ensures age-appropriate jaw/brow/cheek structure while preserving
        /// personal nose, eye and lip genetics.
        /// </summary>
        internal static FaceLatent ProjectFaceLatent(MorphologyGenerationSpec ms, BodyLatent body, FaceLatent genetic, IRandomSource rng)
        {
            var correlated = GenerateFaceLatent(ms, body, rng);
            return correlated with
            {
                NoseScale = genetic.NoseScale,
                EyeScale = genetic.EyeScale,
                LipFullness = genetic.LipFullness
            };
        }

        internal static FaceLatent GenerateFaceLatent(MorphologyGenerationSpec ms, BodyLatent b, IRandomSource rng)
        {
            var robust = C01(0.48 + b.Robustness * 0.40 + b.SexDimorphism * ms.SexBiasStrength - b.Juvenility * 0.35 + b.Aging * 0.08 + Normal(rng) * 0.13);
            var soft = C01(0.50 + b.SoftTissueFullness * 0.35 - robust * 0.22 + b.Juvenility * 0.28 - b.Aging * 0.12 + Normal(rng) * 0.12);
            var projection = C01(0.46 + robust * 0.12 + b.Aging * 0.10 + Normal(rng) * 0.15);
            var angular = C01(0.42 + robust * 0.36 - soft * 0.22 + Normal(rng) * 0.14);
            var brow = C01(0.38 + robust * 0.32 + b.SexDimorphism * 0.18 - b.Juvenility * 0.24 + Normal(rng) * 0.10);
            var nose = C01(0.40 + projection * 0.30 + b.Aging * 0.18 - b.Juvenility * 0.26 + Normal(rng) * 0.13);
            var eye = C01(0.50 + b.Juvenility * 0.26 - robust * 0.10 + Normal(rng) * 0.12);
            var lips = C01(0.50 + soft * 0.25 - b.Aging * 0.18 - b.SexDimorphism * 0.08 + Normal(rng) * 0.14);

            return new(robust, soft, projection, angular, brow, nose, eye, lips, b.Juvenility, b.Aging, b.SexDimorphism);
        }

        internal static BodyMorphology GenerateBody(BodyLatent l, IRandomSource rng)
        {
            var h = l.HeightCm;
            var sitting = h * Clamp(0.52 + l.Juvenility * 0.08 - l.Vertical * 0.025 + J(rng, 0.018), 0.49, 0.64);
            var leg = h - sitting;
            var upperArm = h * Clamp(0.185 + l.Vertical * 0.008 + J(rng, 0.010), 0.16, 0.21);
            var forearm = h * Clamp(0.158 + l.Vertical * 0.007 + J(rng, 0.010), 0.14, 0.18);
            var arm = upperArm + forearm + h * 0.105;
            var neckLen = h * Clamp(0.052 - l.Juvenility * 0.008 + J(rng, 0.008), 0.035, 0.065);
            var clavicle = h * Clamp(0.190 + l.Robustness * 0.025 + l.SexDimorphism * 0.012 + J(rng, 0.018), 0.155, 0.235);
            var ribW = h * Clamp(0.190 + l.Robustness * 0.020 + l.Horizontal * 0.010 + J(rng, 0.016), 0.155, 0.230);
            var ribD = h * Clamp(0.115 + l.Robustness * 0.025 + l.Adiposity * 0.014 + J(rng, 0.012), 0.085, 0.165);
            var pelvis = h * Clamp(0.195 + l.LowerMass * 0.022 - l.SexDimorphism * 0.010 + J(rng, 0.018), 0.160, 0.245);
            var chest = ribW + l.Muscularity * 2.2 + l.SexDimorphism * 1.4;
            var shoulder = clavicle + ribW * 0.26 + l.Muscularity * 2.0 + J(rng, 1.0);
            var waistBase = Lerp(0.55, ribW, pelvis) + l.Adiposity * 2.8 - l.SexDimorphism * 0.5;
            var neckThick = h * Clamp(0.060 + l.Robustness * 0.025 + l.Muscularity * 0.010 + l.SexDimorphism * 0.010, 0.045, 0.090);
            var hand = h * Clamp(0.103 + l.Robustness * 0.010 + J(rng, 0.006), 0.090, 0.120);
            var foot = h * Clamp(0.145 + l.Robustness * 0.012 + J(rng, 0.008), 0.125, 0.170);
            var upperFat = C01(l.Adiposity * (0.75 + l.SexDimorphism * 0.10) + J(rng, 0.10));
            var midFat = C01(l.Adiposity * (0.95 + l.Aging * 0.15) + J(rng, 0.12));
            var lowerFat = C01(l.Adiposity * (0.85 - l.SexDimorphism * 0.20) + l.LowerMass * 0.25 + J(rng, 0.12));
            var firm = C01(0.78 - l.Aging * 0.35 - l.Adiposity * 0.10 + l.Muscularity * 0.16 + J(rng, 0.08));
            var hip = pelvis + lowerFat * 4.0 + l.LowerMass * 2.6 + J(rng, 0.8);
            var waist = waistBase + midFat * 3.2 + J(rng, 0.8);
            var abdomen = C01(0.22 + midFat * 0.45 + l.Aging * 0.10 + J(rng, 0.08));
            var gluteal = C01(0.32 + lowerFat * 0.42 + l.LowerMass * 0.22 + J(rng, 0.08));
            var thigh = C01(0.30 + lowerFat * 0.36 + l.Muscularity * 0.18 + l.LowerMass * 0.20 + J(rng, 0.08));
            var calf = C01(0.28 + l.Muscularity * 0.30 + l.Robustness * 0.16 + J(rng, 0.08));
            var upperArmVol = C01(0.28 + l.Muscularity * 0.34 + upperFat * 0.22 + J(rng, 0.08));
            var forearmVol = C01(0.26 + l.Muscularity * 0.24 + l.Robustness * 0.18 + J(rng, 0.08));

            var proportions = new BodyProportions(R1(h), R1(sitting), R1(leg), R1(sitting), R1(arm), R1(forearm), R1(upperArm), R1(neckLen), R3(shoulder / hip), R3(waist / hip), R3(chest / waist), R3(leg / sitting));
            var skeletal = new SkeletalMorphology(R01(l.Robustness), R1(clavicle), R1(shoulder), R1(ribW), R1(ribD), R1(chest), R1(pelvis), R1(waistBase), R1(neckThick), R1(hand), R1(foot));
            var soft = new SoftTissueMorphology(R01(l.Muscularity), R01(l.Adiposity), R01(firm), R01(l.SoftTissueFullness + l.Adiposity * 0.22), R01(upperFat), R01(midFat), R01(lowerFat), R01(abdomen), R01(gluteal), R01(thigh), R01(calf), R01(upperArmVol), R01(forearmVol));
            var silhouette = new RegionalSilhouetteMorphology(R1(waist), R1(hip), R01(gluteal), R01(thigh * 0.85 + lowerFat * 0.15), R01(calf * 0.75 + l.Muscularity * 0.25), R01(0.48 - l.Robustness * 0.12 + l.Aging * 0.08 + J(rng, 0.08)), R01(0.34 + ribD / h * 2.5 + l.Muscularity * 0.18 + upperFat * 0.10), R01(abdomen), R01(0.30 + upperFat * 0.42 - l.SexDimorphism * 0.18 + J(rng, 0.08)), R01(0.42 + l.LowerMass * 0.28 - l.SexDimorphism * 0.12 + J(rng, 0.08)), R01(0.50 + (hip - waist) / Math.Max(1.0, hip) + J(rng, 0.06)));
            var posture = new PostureMorphology(R01(l.Posture), R01(0.30 + l.Aging * 0.18 - l.Muscularity * 0.08 + J(rng, 0.10)), R01(0.42 + l.Adiposity * 0.08 + l.Aging * 0.08 + J(rng, 0.10)), R01(0.34 + l.Aging * 0.22 + (1.0 - l.Posture) * 0.16 + J(rng, 0.08)), R01(0.22 + l.Aging * 0.24 + (1.0 - l.Posture) * 0.18 + J(rng, 0.08)), R01(0.50 + (lowerFat - upperFat) * 0.08 + J(rng, 0.06)));

            return new BodyMorphology(proportions, skeletal, soft, silhouette, posture);
        }

        internal static FacialMorphology GenerateFace(BodyMorphology body, MorphologyGenerationSpec ms, FaceLatent l, IRandomSource rng)
        {
            var h = body.Proportions.HeightCm;
            var headH = h * Clamp(0.135 + l.Juvenility * 0.105 + J(rng, 0.008), 0.125, 0.245);
            var headW = headH * Clamp(0.73 + l.Robustness * 0.10 + l.Angularity * 0.04 + J(rng, 0.035), 0.66, 0.86);
            var headD = headH * Clamp(0.69 + l.MidfaceProjection * 0.08 + J(rng, 0.025), 0.62, 0.78);
            var faceH = headH * Clamp(0.79 - l.Juvenility * 0.06 + l.Aging * 0.025 + J(rng, 0.025), 0.68, 0.86);
            var faceW = headW * Clamp(0.90 + l.Robustness * 0.08 + J(rng, 0.035), 0.78, 1.03);
            var lowerShare = Clamp(0.335 + l.Robustness * 0.035 + l.Aging * 0.018 - l.Juvenility * 0.06 + J(rng, 0.018), 0.25, 0.40);
            var upperShare = Clamp(0.325 + l.Juvenility * 0.040 + J(rng, 0.014), 0.28, 0.39);
            var midShare = Math.Max(0.22, 1.0 - lowerShare - upperShare);
            var cheekW = faceW * Clamp(0.96 + l.Angularity * 0.08 + J(rng, 0.025), 0.88, 1.06);
            var jawW = faceW * Clamp(0.67 + l.Robustness * 0.22 + l.Angularity * 0.08 - l.Softness * 0.04 + J(rng, 0.035), 0.58, 0.93);
            var templeW = faceW * Clamp(0.83 + l.Robustness * 0.04 + J(rng, 0.025), 0.75, 0.94);
            var chinW = jawW * Clamp(0.42 + l.Robustness * 0.17 + J(rng, 0.035), 0.35, 0.65);
            var chinH = faceH * Clamp(0.12 + l.Robustness * 0.035 - l.Juvenility * 0.035 + J(rng, 0.012), 0.08, 0.17);
            var mandible = Clamp(126 - l.Robustness * 22 + l.Softness * 10 + l.Juvenility * 10 + J(rng, 6), ms.Bounds.MandibleAngle.Min, ms.Bounds.MandibleAngle.Max);

            var cranio = new CraniofacialStructure(R1(headW), R1(headH), R1(headD), R1(faceW), R1(faceH), R3(faceW / faceH), R1(faceH * upperShare), R1(faceH * midShare), R1(faceH * lowerShare), R1(faceH * (0.25 + l.Juvenility * 0.04 + J(rng, 0.012))), R1(templeW), R1(cheekW), R1(cheekW * 1.01), R1(jawW), Math.Round(mandible, 1), R1(chinW), R1(chinH), R01(0.38 + l.Robustness * 0.24 + l.Angularity * 0.10 - l.Juvenility * 0.12 + J(rng, 0.07)), R01(0.38 + l.MidfaceProjection * 0.18 + l.Robustness * 0.10 + l.Aging * 0.08 + J(rng, 0.07)));
            var brow = new ForeheadBrowMorphology(R01(l.BrowProminence), R01(0.58 - l.BrowProminence * 0.16 + l.Juvenility * 0.10 + J(rng, 0.08)), R01(0.38 + l.Robustness * 0.20 + l.SexDimorphism * 0.08 + J(rng, 0.10)), R01(0.50 + l.Angularity * 0.12 + J(rng, 0.12)), R01(0.48 + l.Softness * 0.16 - l.Robustness * 0.08 + J(rng, 0.12)), R01(0.34 + l.BrowProminence * 0.22 + l.Aging * 0.04 + J(rng, 0.10)), R01(0.36 + l.Aging * 0.24 - l.Juvenility * 0.05 + J(rng, 0.10)), R01(0.50 + l.Softness * 0.10 + J(rng, 0.12)));

            var eyeW = cranio.FaceWidth * Clamp(0.165 + l.EyeScale * 0.025 + J(rng, 0.012), 0.135, 0.215);
            var eyeH = eyeW * Clamp(0.34 + l.EyeScale * 0.08 + l.Juvenility * 0.06 + J(rng, 0.035), 0.27, 0.48);
            var eyeSpacing = Math.Max(cranio.FaceWidth * Clamp(0.205 + eyeW / cranio.FaceWidth * 0.25 + J(rng, 0.018), 0.18, 0.34), eyeW * 0.85);
            var eyes = new EyeRegionMorphology(R01(l.EyeScale), R1(eyeW), R1(eyeH), R1(eyeSpacing), R01(0.50 + l.BrowProminence * 0.16 - l.Juvenility * 0.08 + J(rng, 0.08)), R01(0.50 + J(rng, 0.14)), R01(0.50 + l.Angularity * 0.08 + J(rng, 0.14)), R01(0.55 + l.Juvenility * 0.10 - l.Aging * 0.10 + J(rng, 0.10)), R01(0.42 + l.Softness * 0.18 + l.Aging * 0.08 + J(rng, 0.10)), R01(0.50 + l.Juvenility * 0.12 + J(rng, 0.08)), R01(0.42 + l.Softness * 0.14 - l.SexDimorphism * 0.04 + J(rng, 0.12)));

            var noseBaseW = cranio.FaceWidth * Clamp(0.185 + l.NoseScale * 0.052 + J(rng, 0.018), 0.15, 0.29);
            var bridgeW = noseBaseW * Clamp(0.42 + l.Robustness * 0.08 + J(rng, 0.05), 0.34, 0.58);
            var nostril = noseBaseW * Clamp(0.32 + l.NoseScale * 0.10 + J(rng, 0.05), 0.22, 0.48);
            var nose = new NoseMorphology(R01(0.34 + l.NoseScale * 0.42 + l.Aging * 0.08 - l.Juvenility * 0.14 + J(rng, 0.07)), R1(noseBaseW), R01(0.30 + l.MidfaceProjection * 0.32 + l.Robustness * 0.10 + J(rng, 0.08)), R1(bridgeW), R01(l.NoseScale), R01(0.34 + l.NoseScale * 0.40 + l.Aging * 0.07 + J(rng, 0.08)), R01(0.48 + l.Softness * 0.18 - l.Angularity * 0.10 + J(rng, 0.10)), R1(noseBaseW), R1(nostril), R01(0.36 + (noseBaseW / Math.Max(1.0, cranio.FaceWidth)) * 1.35 + J(rng, 0.08)), Math.Round(Clamp(102 - l.NoseScale * 11 + l.Softness * 6 + J(rng, 5), ms.Bounds.NasolabialAngle.Min, ms.Bounds.NasolabialAngle.Max), 1));

            var mouthW = cranio.FaceWidth * Clamp(0.38 + cranio.LowerFaceHeight / cranio.FaceHeight * 0.10 + l.Softness * 0.04 + J(rng, 0.025), 0.33, 0.52);
            var upperLip = C01(l.LipFullness * 0.86 + l.Softness * 0.10 + J(rng, 0.08));
            var lowerLip = C01(l.LipFullness * 1.05 + l.Softness * 0.08 + J(rng, 0.08));
            var mouth = new MouthMorphology(R1(mouthW), R01(0.44 + l.Aging * 0.08 - l.Juvenility * 0.08 + J(rng, 0.08)), R01(upperLip), R01(lowerLip), R01(0.54 + l.Angularity * 0.12 - l.Aging * 0.08 + J(rng, 0.10)), R01(0.46 + upperLip * 0.18 + l.Softness * 0.10 + J(rng, 0.12)), R01(0.50 + J(rng, 0.12)), R01(0.22 + (upperLip + lowerLip) * 0.31));

            var cheeks = new CheekSoftTissueMorphology(R01(0.36 + l.Softness * 0.40 + l.Juvenility * 0.22 - l.Aging * 0.10 + J(rng, 0.08)), R01(0.34 + l.Softness * 0.36 + l.Juvenility * 0.20 - l.Angularity * 0.12 + J(rng, 0.08)), R01(0.38 + l.Angularity * 0.22 + l.Robustness * 0.08 + J(rng, 0.10)), R01(l.Softness), R01(l.Angularity), R01(0.42 + l.Softness * 0.35 + J(rng, 0.08)));
            var jaw = new JawMorphology(R01(0.34 + l.Robustness * 0.36 + l.Angularity * 0.12 - l.Juvenility * 0.12 + J(rng, 0.08)), R01(0.62 + l.Softness * 0.22 - l.Angularity * 0.24 + l.Juvenility * 0.10 + J(rng, 0.08)), R1(jawW * Clamp(0.92 + l.Robustness * 0.12 + J(rng, 0.04), 0.84, 1.10)), R01(0.36 + l.Robustness * 0.30 + l.Aging * 0.08 - l.Juvenility * 0.16 + J(rng, 0.08)));
            var ears = new EarMorphology(R01(0.38 + l.Aging * 0.20 + l.Robustness * 0.10 + J(rng, 0.10)), R01(0.40 + l.Robustness * 0.10 + J(rng, 0.12)), R01(0.36 + l.Aging * 0.12 + J(rng, 0.12)), R01(0.50 + J(rng, 0.12)));
            var asym = new AsymmetryMorphology(R01(Clamp(ms.AsymmetryMean + Normal(rng) * 0.025 + l.Aging * 0.015, ms.Bounds.FacialAsymmetry.Min, ms.Bounds.FacialAsymmetry.Max)), R01(Clamp(ms.AsymmetryMean * 0.85 + Normal(rng) * 0.018, 0.0, 0.13)), R01(Clamp(ms.AsymmetryMean * 0.60 + Normal(rng) * 0.016, 0.0, 0.10)));

            return new FacialMorphology(cranio, brow, eyes, nose, mouth, cheeks, jaw, ears, asym);
        }

        /// <summary>
        /// Generates surface traits. Baby-like state is detected from <c>face.Juvenility >= 0.90</c>
        /// instead of <c>StadiumType.Baby</c> — same threshold, continuous-age compatible.
        /// </summary>
        internal static SurfaceTraits GenerateSurface(MorphologyGenerationSpec ms, FaceLatent face, BodyLatent body, IRandomSource rng)
        {
            var age = C01(face.Aging);
            var freckle = C01(ms.SurfaceDetailRate * 0.65 + Normal(rng) * 0.10);
            var moleCount = Math.Clamp((int)Math.Round(1 + ms.SurfaceDetailRate * 10 + Math.Abs(Normal(rng)) * 3), 0, 18);

            return new SurfaceTraits(
                SkinSmoothness: R01(0.82 + face.Juvenility * 0.08 - age * 0.38 - body.Adiposity * 0.04 + J(rng, 0.08)),
                SkinThickness: R01(0.45 + body.Robustness * 0.12 + age * 0.06 + J(rng, 0.08)),
                FreckleDensity: R01(freckle),
                MoleCount: moleCount,
                DistinctiveMarkProbability: R01(0.03 + ms.SurfaceDetailRate * 0.18),
                ScarProbability: R01(face.Juvenility >= 0.90 ? 0.01 : 0.03 + age * 0.08 + ms.SurfaceDetailRate * 0.08),
                WrinkleTendency: R01(0.04 + age * 0.72 + (1.0 - body.Posture) * 0.06),
                AgeSurfaceFactor: R01(age));
        }

        internal static ColorTraits GenerateColors(AppearanceGenSpec spec, IRandomSource rng)
            => new(
                Pick(new[] { SkinTone.Fair, SkinTone.Light, SkinTone.LightMedium, SkinTone.Medium, SkinTone.Tan }, spec.SkinToneWeights, rng),
                Pick(new[] { EyeColor.Brown, EyeColor.Hazel, EyeColor.Green, EyeColor.Blue, EyeColor.Gray }, spec.EyeColorWeights, rng),
                Pick(new[] { HairColorNatural.Black, HairColorNatural.DarkBrown, HairColorNatural.Brown, HairColorNatural.DarkBlond, HairColorNatural.Blond }, spec.HairColorWeights, rng),
                Pick(new[] { HairType.Straight, HairType.Wavy, HairType.Curly }, spec.HairTypeWeights, rng));

        internal static IReadOnlyList<string>? GenerateMarks(SurfaceTraits s)
        {
            var marks = new List<string>();
            if (s.MoleCount >= 8) marks.Add("noticeable mole pattern");
            if (s.FreckleDensity >= 0.55) marks.Add("visible freckles");
            if (s.ScarProbability >= 0.15) marks.Add("minor scar tendency");
            return marks.Count == 0 ? null : marks;
        }

        #endregion Public API

        #region Math helpers

        internal static double Normal(IRandomSource rng) => rng.NextUnit() + rng.NextUnit() + rng.NextUnit() - 1.5;

        internal static double J(IRandomSource rng, double a) => Normal(rng) * a;

        internal static double Lerp(double t, double a, double b) => a + (b - a) * t;

        internal static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        internal static double C01(double v) => Clamp(v, 0.0, 1.0);

        internal static double ClampSigned(double v) => Clamp(v, -1.0, 1.0);

        internal static double R1(double v) => Math.Round(v, 1);

        internal static double R3(double v) => Math.Round(v, 3);

        internal static double R01(double v) => Math.Round(C01(v), 3);

        internal static T Pick<T>(IReadOnlyList<T> values, IReadOnlyList<double> weights, IRandomSource rng)
        {
            double s = 0;
            var r = rng.NextUnit();
            for (var i = 0; i < values.Count; i++)
            {
                s += weights[i];
                if (r <= s) return values[i];
            }

            return values[^1];
        }

        #endregion Math helpers
    }
}
