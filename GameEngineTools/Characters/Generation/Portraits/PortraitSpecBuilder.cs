// PortraitSpecBuilder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Deterministically maps stable character data to a strict portrait specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All bucket helpers are pure functions — no randomness, no side effects.
    /// The same input always produces the same <see cref="PortraitSpec"/>.
    /// </para>
    /// <para>
    /// Raw centimetre values are preserved in <see cref="BodyRenderSpec"/> for data fidelity,
    /// but the formatter must use bucket labels rather than raw numbers.
    /// Image generation models do not understand centimetre measurements.
    /// </para>
    /// </remarks>
    public sealed class PortraitSpecBuilder : IPortraitSpecBuilder
    {
        #region Constants

        /// <summary>
        /// Default bias guard — all beautification flags are <c>true</c>.
        /// Applied to every generated spec; relax per-character only when the
        /// simulation scenario explicitly warrants it (e.g. a glamour context).
        /// </summary>
        private static readonly PortraitBiasGuard DefaultBiasGuard = new(
            ForbidSymmetryEnhancement: true,
            ForbidSkinSmoothing: true,
            ForbidEyeEnlargement: true,
            ForbidLipEnhancement: true,
            ForbidAestheticReinterpretation: true,
            ForbidForcedSmile: true);

        #endregion Constants

        #region IPortraitSpecBuilder

        /// <inheritdoc/>
        public PortraitSpec Build(
            SexBiology biology,
            PhysicalAppearance appearance,
            int ageYears,
            string? ancestryHint = null,
            EnginesSnapshot? snapshot = null)
        {
            ArgumentNullException.ThrowIfNull(appearance);

            return new PortraitSpec(
                Biology:        biology,
                AgeLabel:       BuildAgeLabel(biology, ageYears),
                AncestryHint:   ancestryHint,
                Body:           BuildBody(appearance),
                Skin:           BuildSkin(appearance.Colors.SkinTone),
                Eyes:           BuildEyes(appearance.Colors.EyeColor),
                Hair:           BuildHair(appearance.Colors.HairColor, appearance.Colors.HairType, appearance.HairLengthCm),
                Face:           BuildFace(appearance),
                Expression:     BuildExpression(snapshot),
                BiasGuard:      DefaultBiasGuard,
                DistinctiveMarks: BuildDistinctiveMarks(appearance.DistinctiveMarks));
        }

        #endregion IPortraitSpecBuilder

        #region Sub-builders

        /// <summary>
        /// Maps age in years to a natural-language descriptor.
        /// </summary>
        /// <remarks>
        /// Uses decade ranges deliberately — image models interpret age as a visual category,
        /// not a precise number. "woman in her late 30s" produces more consistent results
        /// than "woman aged 37".
        /// </remarks>
        /// <param name="biology">Biological sex — controls noun choice in the label.</param>
        /// <param name="ageYears">Character's current age in years.</param>
        /// <returns>Natural-language age label for image generation.</returns>
        private static string BuildAgeLabel(SexBiology biology, int ageYears)
        {
            // Gender-appropriate noun for natural phrasing
            var noun = biology == SexBiology.Female ? "woman" : "man";

            return ageYears switch
            {
                < 3  => "toddler",
                < 12 => "child",
                < 16 => "young teenager",
                < 20 => "older teenager",
                < 25 => $"young {noun} in early 20s",
                < 30 => $"{noun} in mid-to-late 20s",
                < 35 => $"{noun} in early 30s",
                < 40 => $"{noun} in mid-to-late 30s",
                < 45 => $"{noun} in early 40s",
                < 55 => $"{noun} in late 40s to early 50s",
                < 65 => $"{noun} in late 50s to early 60s",
                < 75 => $"older {noun}, mid 60s to early 70s",
                _    => $"elderly {noun}, 75 or older"
            };
        }

        /// <summary>Builds body proportion and frame specification from raw morphology data.</summary>
        /// <param name="appearance">Full physical appearance record.</param>
        /// <returns>Body render specification with bucket labels.</returns>
        private static BodyRenderSpec BuildBody(PhysicalAppearance appearance)
        {
            var heightCm        = appearance.Body.Proportions.HeightCm;
            var shoulderBreadth = appearance.Body.Skeletal.ShoulderBreadth;
            var hipBreadth      = appearance.Body.Silhouette.HipWidth;
            var frame           = DeriveFrame(appearance.Body);
            var shoulderToHeight = shoulderBreadth / heightCm;
            var hipToHeight      = hipBreadth      / heightCm;
            var shoulderHipDelta = shoulderBreadth - hipBreadth;

            return new BodyRenderSpec(
                HeightCm:          Math.Round(heightCm, 2),
                ShoulderBreadthCm: Math.Round(shoulderBreadth, 2),
                HipBreadthCm:      Math.Round(hipBreadth, 2),
                WaistToHipRatio:   Math.Round(appearance.Body.Proportions.WaistToHipRatio, 3),
                Frame:             frame,
                HeightBucket:      BucketHeight(heightCm),
                ProportionBucket:  BucketProportions(shoulderToHeight, hipToHeight),
                PostureBucket:     BucketPosture(appearance.Body.Posture.PostureUprightness),
                FrameImpression:   BucketFrameImpression(frame, shoulderHipDelta));
        }

        /// <summary>Maps a <see cref="SkinTone"/> enum to a natural-language skin render spec.</summary>
        private static SkinRenderSpec BuildSkin(SkinTone tone) => tone switch
        {
            SkinTone.VeryFair    => new("very fair",     "very light",   "cool-neutral",  "natural skin texture visible", true, false),
            SkinTone.Fair        => new("fair",          "light",        "neutral",        "natural skin texture visible", true, false),
            SkinTone.Light       => new("light",         "light",        "neutral-warm",   "natural skin texture visible", true, false),
            SkinTone.LightMedium => new("light-medium",  "light-medium", "warm",           "natural skin texture visible", true, false),
            SkinTone.Medium      => new("medium",        "medium",       "neutral",        "natural skin texture visible", true, false),
            SkinTone.Tan         => new("tan",           "medium-dark",  "warm",           "natural skin texture visible", true, false),
            SkinTone.Dark        => new("dark",          "dark",         "neutral-cool",   "natural skin texture visible", true, false),
            SkinTone.VeryDark    => new("very dark",     "very dark",    "neutral-cool",   "natural skin texture visible", true, false),
            SkinTone.Olive       => new("olive",         "light-medium", "olive",          "natural skin texture visible", true, false),
            _                    => new("medium",        "medium",       "neutral",        "natural skin texture visible", true, false)
        };

        /// <summary>Maps an <see cref="EyeColor"/> enum to a natural-language eye render spec.</summary>
        private static EyeRenderSpec BuildEyes(EyeColor color) => color switch
        {
            EyeColor.Brown  => new("brown",  "low variation",      "medium", "do not enlarge eyes"),
            EyeColor.Hazel  => new("hazel",  "moderate variation", "medium", "do not enlarge eyes"),
            EyeColor.Green  => new("green",  "moderate variation", "medium", "do not enlarge eyes"),
            EyeColor.Blue   => new("blue",   "low variation",      "medium", "do not enlarge eyes"),
            EyeColor.Gray   => new("gray",   "low variation",      "soft",   "do not enlarge eyes"),
            EyeColor.Amber  => new("amber",  "low variation",      "medium", "do not enlarge eyes"),
            _               => new("brown",  "low variation",      "medium", "do not enlarge eyes")
        };

        /// <summary>Maps hair colour, type and length data to a natural-language hair render spec.</summary>
        private static HairRenderSpec BuildHair(HairColorNatural color, HairType type, double lengthCm)
        {
            var (baseColorFamily, brightnessRange) = color switch
            {
                HairColorNatural.Black      => ("black",      "very dark"),
                HairColorNatural.DarkBrown  => ("dark brown", "dark"),
                HairColorNatural.Brown      => ("brown",      "medium"),
                HairColorNatural.Auburn     => ("auburn",     "medium"),
                HairColorNatural.Red        => ("red",        "medium"),
                HairColorNatural.Blond      => ("blond",      "light"),
                HairColorNatural.DarkBlond  => ("dark blond", "medium-light"),
                _                           => ("brown",      "medium")
            };

            var (texture, straightness) = type switch
            {
                HairType.Straight => ("smooth strand texture", "straight"),
                HairType.Wavy     => ("soft wave texture",     "wavy"),
                HairType.Curly    => ("defined curl texture",  "curly"),
                HairType.Coily    => ("tight coil texture",    "coily"),
                _                 => ("smooth strand texture", "straight")
            };

            return new HairRenderSpec(
                BaseColorFamily: baseColorFamily,
                BrightnessRange: brightnessRange,
                Texture:         texture,
                Straightness:    straightness,
                LengthBucket:    BucketHairLength(lengthCm),
                VolumePolicy:    "natural volume only");
        }

        /// <summary>
        /// Maps craniofacial and feature measurements to a natural-language face render spec.
        /// </summary>
        private static FaceRenderSpec BuildFace(PhysicalAppearance appearance)
        {
            var faceShape  = DeriveFaceShape(appearance.Face.Craniofacial);
            var lipFullness = (appearance.Face.Mouth.UpperLipFullness + appearance.Face.Mouth.LowerLipFullness) * 0.5;

            return new FaceRenderSpec(
                ShapeLabel:            FaceShapeLabel(faceShape),
                WidthHeightTendency:   FaceWidthHeightTendency(faceShape),
                NoseProjectionBucket:  BucketNoseProjection(appearance.Face.Nose.NoseProjection),
                LipFullnessBucket:     BucketLipFullness(lipFullness),
                EyeScaleBucket:        BucketEyeScale(appearance.Face.EyeRegion.EyeSize),
                JawDefinitionBucket:   BucketJawDefinition(appearance.Face.Jaw.JawProminence, appearance.Face.Jaw.JawRoundness),
                FacialAsymmetryBucket: BucketFacialAsymmetry(appearance.Face.Asymmetry.FacialAsymmetry),
                SymmetryPolicy:        "preserve natural asymmetry");
        }

        /// <summary>
        /// Resolves expression from the runtime snapshot.
        /// Falls back to <see cref="PortraitExpressionKind.Neutral"/> when snapshot is null.
        /// </summary>
        private static ExpressionRenderSpec BuildExpression(EnginesSnapshot? snapshot)
        {
            var kind = ResolveExpression(snapshot);

            return kind switch
            {
                PortraitExpressionKind.Calm  => new(kind, "calm",    "closed mouth", "relaxed brows",        false),
                PortraitExpressionKind.Alert => new(kind, "alert",   "closed mouth", "slight brow lift",     false),
                PortraitExpressionKind.Tired => new(kind, "tired",   "closed mouth", "soft brow tension",    false),
                PortraitExpressionKind.Tense => new(kind, "tense",   "closed mouth", "visible brow tension", false),
                _                            => new(PortraitExpressionKind.Neutral, "neutral", "closed mouth", "neutral brows", false)
            };
        }

        /// <summary>Strips nulls and whitespace from the raw distinctive marks list.</summary>
        private static IReadOnlyList<string> BuildDistinctiveMarks(IReadOnlyList<string>? marks)
        {
            if (marks is null || marks.Count == 0)
                return Array.Empty<string>();

            return marks
                .Where(static v => !string.IsNullOrWhiteSpace(v))
                .Select(static v => v.Trim())
                .ToArray();
        }

        #endregion Sub-builders

        #region Expression resolver

        /// <summary>
        /// Derives a conservative expression category from the runtime snapshot.
        /// Priority order: Tired > Tense > Alert > Calm > Neutral.
        /// </summary>
        private static PortraitExpressionKind ResolveExpression(EnginesSnapshot? snapshot)
        {
            if (snapshot is null)
                return PortraitExpressionKind.Neutral;

            // Sleep debt or extreme fatigue overrides everything
            if (snapshot.Physiology.SleepDebtHours >= 10 || snapshot.Behavior.NeedRest >= 80)
                return PortraitExpressionKind.Tired;

            // High stress produces visible tension
            if (snapshot.Psychology.Stress >= 70)
                return PortraitExpressionKind.Tense;

            // High arousal or surprise produces alert wide-eye look
            if (snapshot.Psychology.Arousal >= 0.72 || snapshot.Psychology.DominantEmotion == DiscreteEmotion.Surprise)
                return PortraitExpressionKind.Alert;

            // Explicitly calm: low stress, low arousal, neutral emotion
            if (snapshot.Psychology.Stress <= 30 &&
                snapshot.Psychology.Arousal <= 0.4  &&
                snapshot.Psychology.DominantEmotion == DiscreteEmotion.Neutral)
            {
                return PortraitExpressionKind.Calm;
            }

            return PortraitExpressionKind.Neutral;
        }

        #endregion Expression resolver

        #region Bucket helpers — body

        /// <summary>Maps height in cm to a short natural-language label.</summary>
        private static string BucketHeight(double heightCm) => heightCm switch
        {
            < 155 => "short",
            < 171 => "medium height",
            _     => "tall"
        };

        /// <summary>Maps shoulder-to-height and hip-to-height ratios to a proportion label.</summary>
        private static string BucketProportions(double shoulderToHeight, double hipToHeight)
        {
            var average = (shoulderToHeight + hipToHeight) * 0.5;

            return average switch
            {
                < 0.220 => "narrow proportions",
                < 0.245 => "balanced proportions",
                _       => "broad proportions"
            };
        }

        /// <summary>Maps frame + shoulder-hip delta to a natural-language impression label.</summary>
        private static string BucketFrameImpression(BodyFrame frame, double shoulderHipDelta) => frame switch
        {
            BodyFrame.Petite => shoulderHipDelta <= -1.0
                ? "petite frame with slightly hip-led silhouette"
                : "petite frame",

            BodyFrame.Medium => Math.Abs(shoulderHipDelta) < 1.0
                ? "medium frame with balanced silhouette"
                : "medium frame",

            BodyFrame.Large  => shoulderHipDelta >= 1.0
                ? "large frame with shoulder-led silhouette"
                : "large frame",

            BodyFrame.Strong => shoulderHipDelta >= 1.0
                ? "strong frame with shoulder-led silhouette"
                : "strong frame",

            _ => "medium frame"
        };

        /// <summary>Maps posture uprightness (0–1) to a natural-language posture label.</summary>
        private static string BucketPosture(double postureUprightness) => postureUprightness switch
        {
            < 0.40 => "noticeably slouched carriage",
            < 0.62 => "softly relaxed carriage",
            < 0.82 => "neutral upright carriage",
            _      => "very upright carriage"
        };

        #endregion Bucket helpers — body

        #region Bucket helpers — hair

        /// <summary>Maps hair length in cm to a natural-language length label.</summary>
        private static string BucketHairLength(double lengthCm) => lengthCm switch
        {
            < 2.0  => "shaved or very short",
            < 10.0 => "short",
            < 30.0 => "medium length",
            < 65.0 => "long",
            _      => "very long"
        };

        #endregion Bucket helpers — hair

        #region Bucket helpers — face

        /// <summary>Returns the natural-language label for a derived face shape.</summary>
        private static string FaceShapeLabel(FaceShape faceShape) => faceShape switch
        {
            FaceShape.Oval    => "oval",
            FaceShape.Round   => "round",
            FaceShape.Square  => "square",
            FaceShape.Heart   => "heart",
            FaceShape.Diamond => "diamond",
            FaceShape.Oblong  => "oblong",
            _                 => "oval"
        };

        /// <summary>Returns the width-to-height tendency label for a face shape.</summary>
        private static string FaceWidthHeightTendency(FaceShape faceShape) => faceShape switch
        {
            FaceShape.Oval    => "balanced width-to-height tendency",
            FaceShape.Round   => "wider-than-tall tendency",
            FaceShape.Square  => "broad width with defined jaw tendency",
            FaceShape.Heart   => "broader upper face with narrower jaw tendency",
            FaceShape.Diamond => "widest at cheek level tendency",
            FaceShape.Oblong  => "longer-than-wide tendency",
            _                 => "balanced width-to-height tendency"
        };

        /// <summary>Maps nose projection (0–1) to a natural-language bucket.</summary>
        private static string BucketNoseProjection(double prominence) => prominence switch
        {
            < 0.25 => "low projection",
            < 0.45 => "moderate-low projection",
            < 0.65 => "moderate projection",
            < 0.80 => "moderate-high projection",
            _      => "high projection"
        };

        /// <summary>Maps average lip fullness (0–1) to a natural-language bucket.</summary>
        private static string BucketLipFullness(double fullness) => fullness switch
        {
            < 0.25 => "thin",
            < 0.45 => "medium-thin",
            < 0.65 => "medium-full",
            < 0.80 => "full",
            _      => "very full"
        };

        /// <summary>Maps eye size (0–1) to a natural-language bucket.</summary>
        private static string BucketEyeScale(double eyeSize) => eyeSize switch
        {
            < 0.36 => "small eye scale",
            < 0.62 => "medium eye scale",
            _      => "large eye scale"
        };

        /// <summary>Maps jaw prominence and roundness to a natural-language definition label.</summary>
        private static string BucketJawDefinition(double jawProminence, double jawRoundness)
        {
            if (jawProminence >= 0.62 && jawRoundness <= 0.48)
                return "angular jaw definition";

            if (jawProminence <= 0.38 && jawRoundness >= 0.58)
                return "soft rounded jaw definition";

            return "moderate jaw definition";
        }

        /// <summary>Maps facial asymmetry (0–1) to a natural-language bucket.</summary>
        private static string BucketFacialAsymmetry(double asymmetry) => asymmetry switch
        {
            < 0.04 => "very subtle natural asymmetry",
            < 0.09 => "subtle natural asymmetry",
            _      => "noticeable natural asymmetry"
        };

        #endregion Bucket helpers — face

        #region Frame and face shape derivation

        /// <summary>Derives a discrete <see cref="BodyFrame"/> from skeletal and soft-tissue data.</summary>
        private static BodyFrame DeriveFrame(BodyMorphology body)
        {
            var robustness  = body.Skeletal.SkeletalRobustness;
            var muscularity = body.SoftTissue.Muscularity;
            var adiposity   = body.SoftTissue.Adiposity;

            if (muscularity >= 0.68 && robustness >= 0.58)
                return BodyFrame.Strong;

            if (robustness <= 0.38 && adiposity <= 0.48)
                return BodyFrame.Petite;

            return robustness + adiposity * 0.45 >= 0.78 ? BodyFrame.Large : BodyFrame.Medium;
        }

        /// <summary>Derives a discrete <see cref="FaceShape"/> from craniofacial measurements.</summary>
        private static FaceShape DeriveFaceShape(CraniofacialStructure c)
        {
            var ratio      = c.FaceWidthToHeightRatio;
            var jawToFace  = c.JawWidth / Math.Max(1.0, c.FaceWidth);
            var cheekToJaw = c.CheekboneWidth / Math.Max(1.0, c.JawWidth);

            if (ratio >= 0.86 && jawToFace >= 0.80) return FaceShape.Square;
            if (ratio >= 0.86)                       return FaceShape.Round;
            if (ratio <= 0.72)                       return FaceShape.Oblong;
            if (cheekToJaw >= 1.23)                  return FaceShape.Diamond;

            return jawToFace <= 0.68 ? FaceShape.Heart : FaceShape.Oval;
        }

        #endregion Frame and face shape derivation
    }
}
