// TransferenceMath.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System;
    using System.Reflection;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Pure, stateless resemblance math for the transference subsystem (Task C.3).
    /// </summary>
    /// <remarks>
    /// ⚠ Single-lab-origin caution — see <see cref="SignificantOtherImprint"/> remarks.
    /// Combines facial-morphology resemblance (Kraus &amp; Chen 2010; Günaydın, Zayas, Chen &amp;
    /// Hazan 2012 — aggregate d=0.55) with Big Five personality resemblance (Andersen &amp; Cole
    /// 1990; Brumbaugh &amp; Fraley 2006 — independently replicated for attachment-pattern
    /// resemblance specifically, the strongest empirical anchor available for this computation).
    /// </remarks>
    internal static class TransferenceMath
    {
        /// <summary>Typical spread for <see cref="CraniofacialStructure"/>'s centimetre-scale fields.</summary>
        private const double CraniofacialScale = 10.0;

        /// <summary>
        /// Typical spread for the remaining facial sub-morphologies' compact descriptor fields.
        /// Architectural approximation (not measured) — see <see cref="FacialResemblance"/> remarks.
        /// </summary>
        private const double DescriptorScale = 0.5;

        /// <summary>
        /// Facial resemblance in [0, 1], comparing two <see cref="FacialMorphology"/> descriptors.
        /// </summary>
        /// <remarks>
        /// MVP: normalized inverse Euclidean distance across every numeric leaf field of
        /// <see cref="FacialMorphology"/>'s nested sub-morphologies, reached via reflection rather
        /// than hand-enumerated — reuses the existing appearance vector rather than inventing a new
        /// similarity metric (resolves Topic C research question C.2: GET does not need a bespoke
        /// similarity metric), and stays correct if new morphology fields are added later.
        /// The per-field normalization scale (<see cref="CraniofacialScale"/> for raw centimetre
        /// measurements, <see cref="DescriptorScale"/> for the remaining compact descriptors) is an
        /// architectural approximation, not a literature-specified value.
        /// </remarks>
        public static double FacialResemblance(FacialMorphology a, FacialMorphology b)
        {
            var sumSq = 0.0;
            var count = 0;
            AccumulateFieldDistance(a, b, CraniofacialScale, ref sumSq, ref count);

            if (count == 0)
            {
                return 1.0;
            }

            var distance = Math.Sqrt(sumSq / count);
            return Math.Clamp(1.0 - distance, 0.0, 1.0);
        }

        /// <summary>
        /// Recursively walks two structurally-identical <see cref="Traits"/> morphology records,
        /// accumulating a normalized squared difference for every <see cref="double"/> leaf field.
        /// </summary>
        private static void AccumulateFieldDistance(object a, object b, double topLevelScale, ref double sumSq, ref int count)
        {
            var type = a.GetType();
            var scale = type == typeof(CraniofacialStructure) ? topLevelScale : DescriptorScale;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var va = prop.GetValue(a);
                var vb = prop.GetValue(b);

                if (va is double da && vb is double db)
                {
                    var diff = Math.Clamp(Math.Abs(da - db) / scale, 0.0, 1.0);
                    sumSq += diff * diff;
                    count++;
                }
                else if (va is not null && vb is not null && prop.PropertyType.Namespace == typeof(FacialMorphology).Namespace)
                {
                    // Nested sub-morphology record (ForeheadBrow, EyeRegion, Nose, ...) — recurse.
                    AccumulateFieldDistance(va, vb, topLevelScale, ref sumSq, ref count);
                }
            }
        }

        /// <summary>
        /// Personality resemblance in [0, 1], comparing two <see cref="BigFive"/> profiles via
        /// normalized Euclidean distance across the five traits (each already in [0, 1]).
        /// </summary>
        public static double PersonalityResemblance(BigFive a, BigFive b)
        {
            var sumSq =
                Square(a.Openness - b.Openness) +
                Square(a.Conscientiousness - b.Conscientiousness) +
                Square(a.Extraversion - b.Extraversion) +
                Square(a.Agreeableness - b.Agreeableness) +
                Square(a.Neuroticism - b.Neuroticism);

            // Max possible distance across 5 dims each in [0,1] is sqrt(5).
            var distance = Math.Sqrt(sumSq) / Math.Sqrt(5.0);
            return Math.Clamp(1.0 - distance, 0.0, 1.0);
        }

        /// <summary>
        /// Sex-differentiated facial-resemblance weighting, applied on top of the sex-neutral
        /// facial resemblance score.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠ <b>Extrapolation caveat:</b> Günaydın, Zayas, Chen &amp; Hazan (2012, <i>Journal of Research
        /// in Personality</i>) found facial resemblance to a significant other more strongly influenced
        /// women's (d=0.87) than men's (d=0.12) snap judgments — but that study measured
        /// attractiveness/liking judgments, NOT belief-transference (Trust/Warm/Rejecting seeding).
        /// Applying the same ratio here assumes the sex qualification generalizes across dependent
        /// variables, which the cited source does not itself establish. The weighting below is
        /// deliberately damped relative to the source's own d=0.87/d=0.12 ratio for this reason.
        /// </para>
        /// <para>
        /// Gated by <see cref="Relationships.RelationshipsConfig.ApplySexDifferentiatedFacialResemblance"/>
        /// (default <c>true</c> per explicit inclusion instruction) — set <c>false</c> to fall back to the
        /// sex-neutral resemblance path if this extrapolation is reconsidered in a future research pass.
        /// </para>
        /// </remarks>
        public static double SexWeightedFacialResemblance(
            double rawFacialResemblance, SexBiology observerSex, Relationships.RelationshipsConfig cfg)
        {
            if (!cfg.ApplySexDifferentiatedFacialResemblance)
            {
                return rawFacialResemblance;
            }

            var sexMultiplier = observerSex == SexBiology.Female
                ? cfg.FacialResemblanceWeightFemale
                : cfg.FacialResemblanceWeightMale;

            return Math.Clamp(rawFacialResemblance * sexMultiplier, 0.0, 1.0);
        }

        /// <summary>
        /// Combined resemblance score in [0, 1], blending sex-weighted facial and personality
        /// resemblance.
        /// </summary>
        /// <remarks>
        /// The literature does not specify a combination weight between facial and behavioral/
        /// personality resemblance — both are independently shown to trigger transference, not
        /// jointly weighted in any cited study. <paramref name="faceWeight"/> is therefore an
        /// architectural default (0.5/0.5), not a sourced coefficient — flagged explicitly here so
        /// it is never mistaken for a citable constant.
        /// </remarks>
        public static double CombinedResemblance(
            double facialResemblance,
            double personalityResemblance,
            SexBiology observerSex,
            Relationships.RelationshipsConfig cfg,
            double faceWeight = 0.5)
        {
            var weightedFacial = SexWeightedFacialResemblance(facialResemblance, observerSex, cfg);
            return Math.Clamp(
                weightedFacial * faceWeight + personalityResemblance * (1.0 - faceWeight),
                0.0, 1.0);
        }

        private static double Square(double x) => x * x;
    }
}
