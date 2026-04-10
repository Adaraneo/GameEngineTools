// PortraitSpecBuilder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Deterministically maps stable appearance data to a strict portrait specification.
    /// </summary>
    public sealed class PortraitSpecBuilder : IPortraitSpecBuilder
    {
        private static readonly PortraitBiasGuard DefaultBiasGuard = new(
            ForbidSymmetryEnhancement: true,
            ForbidSkinSmoothing: true,
            ForbidEyeEnlargement: true,
            ForbidLipEnhancement: true,
            ForbidAestheticReinterpretation: true,
            ForbidForcedSmile: true);

        /// <inheritdoc/>
        public PortraitSpec Build(SexBiology biology, PhysicalAppearance appearance, EnginesSnapshot? snapshot = null)
        {
            ArgumentNullException.ThrowIfNull(appearance);

            return new PortraitSpec(
                Biology: biology,
                Body: BuildBody(appearance),
                Skin: BuildSkin(appearance.SkinTone),
                Eyes: BuildEyes(appearance.EyeColor),
                Hair: BuildHair(appearance.HairColor, appearance.HairType),
                Face: BuildFace(appearance),
                Expression: BuildExpression(snapshot),
                BiasGuard: DefaultBiasGuard,
                DistinctiveMarks: BuildDistinctiveMarks(appearance.DistinctiveMarks));
        }

        private static IReadOnlyList<string> BuildDistinctiveMarks(IReadOnlyList<string>? marks)
        {
            if (marks is null || marks.Count == 0)
            {
                return Array.Empty<string>();
            }

            return marks
                .Where(static v => !string.IsNullOrWhiteSpace(v))
                .Select(static v => v.Trim())
                .ToArray();
        }

        private static BodyRenderSpec BuildBody(PhysicalAppearance appearance)
        {
            var shoulderToHeight = appearance.ShoulderBreadthCm / appearance.HeightCm;
            var hipToHeight = appearance.HipBreadthCm / appearance.HeightCm;
            var shoulderHipDelta = appearance.ShoulderBreadthCm - appearance.HipBreadthCm;

            return new BodyRenderSpec(
                HeightCm: Math.Round(appearance.HeightCm, 2),
                ShoulderBreadthCm: Math.Round(appearance.ShoulderBreadthCm, 2),
                HipBreadthCm: Math.Round(appearance.HipBreadthCm, 2),
                Frame: appearance.Frame,
                HeightBucket: BucketHeight(appearance.HeightCm),
                ProportionBucket: BucketProportions(shoulderToHeight, hipToHeight),
                FrameImpression: BucketFrameImpression(appearance.Frame, shoulderHipDelta));
        }

        private static SkinRenderSpec BuildSkin(SkinTone tone)
        {
            return tone switch
            {
                SkinTone.VeryFair => new("very fair", "very light", "cool-neutral", "natural skin texture visible", true, false),
                SkinTone.Fair => new("fair", "light", "neutral", "natural skin texture visible", true, false),
                SkinTone.Light => new("light", "light", "neutral-warm", "natural skin texture visible", true, false),
                SkinTone.LightMedium => new("light-medium", "light-medium", "warm", "natural skin texture visible", true, false),
                SkinTone.Medium => new("medium", "medium", "neutral", "natural skin texture visible", true, false),
                SkinTone.Tan => new("tan", "medium-dark", "warm", "natural skin texture visible", true, false),
                SkinTone.Dark => new("dark", "dark", "neutral-cool", "natural skin texture visible", true, false),
                SkinTone.VeryDark => new("very dark", "very dark", "neutral-cool", "natural skin texture visible", true, false),
                SkinTone.Olive => new("olive", "light-medium", "olive", "natural skin texture visible", true, false),
                _ => new("medium", "medium", "neutral", "natural skin texture visible", true, false)
            };
        }

        private static EyeRenderSpec BuildEyes(EyeColor color)
        {
            return color switch
            {
                EyeColor.Brown => new("brown", "low variation", "medium", "do not enlarge eyes"),
                EyeColor.Hazel => new("hazel", "moderate variation", "medium", "do not enlarge eyes"),
                EyeColor.Green => new("green", "moderate variation", "medium", "do not enlarge eyes"),
                EyeColor.Blue => new("blue", "low variation", "medium", "do not enlarge eyes"),
                EyeColor.Gray => new("gray", "low variation", "soft", "do not enlarge eyes"),
                EyeColor.Amber => new("amber", "low variation", "medium", "do not enlarge eyes"),
                _ => new("brown", "low variation", "medium", "do not enlarge eyes")
            };
        }

        private static HairRenderSpec BuildHair(HairColorNatural color, HairType type)
        {
            var (baseColorFamily, brightnessRange) = color switch
            {
                HairColorNatural.Black => ("black", "very dark"),
                HairColorNatural.DarkBrown => ("dark brown", "dark"),
                HairColorNatural.Brown => ("brown", "medium"),
                HairColorNatural.Auburn => ("auburn", "medium"),
                HairColorNatural.Red => ("red", "medium"),
                HairColorNatural.Blond => ("blond", "light"),
                HairColorNatural.DarkBlond => ("dark blond", "medium-light"),
                _ => ("brown", "medium")
            };

            var (texture, straightness) = type switch
            {
                HairType.Straight => ("smooth strand texture", "straight"),
                HairType.Wavy => ("soft wave texture", "wavy"),
                HairType.Curly => ("defined curl texture", "curly"),
                HairType.Coily => ("tight coil texture", "coily"),
                _ => ("smooth strand texture", "straight")
            };

            return new HairRenderSpec(
                BaseColorFamily: baseColorFamily,
                BrightnessRange: brightnessRange,
                Texture: texture,
                Straightness: straightness,
                VolumePolicy: "natural volume only");
        }

        private static FaceRenderSpec BuildFace(PhysicalAppearance appearance)
        {
            return new FaceRenderSpec(
                ShapeLabel: FaceShapeLabel(appearance.FaceShape),
                WidthHeightTendency: FaceWidthHeightTendency(appearance.FaceShape),
                NoseProjectionBucket: BucketNoseProjection(appearance.NoseProminence),
                LipFullnessBucket: BucketLipFullness(appearance.LipFullness),
                SymmetryPolicy: "preserve natural asymmetry");
        }

        private static ExpressionRenderSpec BuildExpression(EnginesSnapshot? snapshot)
        {
            var kind = ResolveExpression(snapshot);

            return kind switch
            {
                PortraitExpressionKind.Calm => new(kind, "calm", "closed mouth", "relaxed brows", false),
                PortraitExpressionKind.Alert => new(kind, "alert", "closed mouth", "slight brow lift", false),
                PortraitExpressionKind.Tired => new(kind, "tired", "closed mouth", "soft brow tension", false),
                PortraitExpressionKind.Tense => new(kind, "tense", "closed mouth", "visible brow tension", false),
                _ => new(PortraitExpressionKind.Neutral, "neutral", "closed mouth", "neutral brows", false)
            };
        }

        private static PortraitExpressionKind ResolveExpression(EnginesSnapshot? snapshot)
        {
            if (snapshot is null)
            {
                return PortraitExpressionKind.Neutral;
            }

            if (snapshot.Physiology.SleepDebtHours >= 10 || snapshot.Behavior.NeedRest >= 80)
            {
                return PortraitExpressionKind.Tired;
            }

            if (snapshot.Psychology.Stress >= 70)
            {
                return PortraitExpressionKind.Tense;
            }

            if (snapshot.Psychology.Arousal >= 0.72 || snapshot.Psychology.DominantEmotion == DiscreteEmotion.Surprise)
            {
                return PortraitExpressionKind.Alert;
            }

            if (snapshot.Psychology.Stress <= 30 &&
                snapshot.Psychology.Arousal <= 0.4 &&
                snapshot.Psychology.DominantEmotion == DiscreteEmotion.Neutral)
            {
                return PortraitExpressionKind.Calm;
            }

            return PortraitExpressionKind.Neutral;
        }

        private static string BucketHeight(double heightCm)
        {
            if (heightCm < 155)
            {
                return "short";
            }

            if (heightCm < 171)
            {
                return "medium height";
            }

            return "tall";
        }

        private static string BucketProportions(double shoulderToHeight, double hipToHeight)
        {
            var averageRatio = (shoulderToHeight + hipToHeight) * 0.5;
            if (averageRatio < 0.22)
            {
                return "narrow proportions";
            }

            if (averageRatio < 0.245)
            {
                return "balanced proportions";
            }

            return "broad proportions";
        }

        private static string BucketFrameImpression(BodyFrame frame, double shoulderHipDelta)
        {
            return frame switch
            {
                BodyFrame.Petite => shoulderHipDelta <= -1.0 ? "petite frame with slightly hip-led silhouette" : "petite frame",
                BodyFrame.Medium => Math.Abs(shoulderHipDelta) < 1.0 ? "medium frame with balanced silhouette" : "medium frame",
                BodyFrame.Large => shoulderHipDelta >= 1.0 ? "large frame with shoulder-led silhouette" : "large frame",
                BodyFrame.Strong => shoulderHipDelta >= 1.0 ? "strong frame with shoulder-led silhouette" : "strong frame",
                _ => "medium frame"
            };
        }

        private static string FaceShapeLabel(FaceShape faceShape)
        {
            return faceShape switch
            {
                FaceShape.Oval => "oval",
                FaceShape.Round => "round",
                FaceShape.Square => "square",
                FaceShape.Heart => "heart",
                FaceShape.Diamond => "diamond",
                FaceShape.Oblong => "oblong",
                _ => "oval"
            };
        }

        private static string FaceWidthHeightTendency(FaceShape faceShape)
        {
            return faceShape switch
            {
                FaceShape.Oval => "balanced width-to-height tendency",
                FaceShape.Round => "wider-than-tall tendency",
                FaceShape.Square => "broad width with defined jaw tendency",
                FaceShape.Heart => "broader upper face with narrower jaw tendency",
                FaceShape.Diamond => "widest at cheek level tendency",
                FaceShape.Oblong => "longer-than-wide tendency",
                _ => "balanced width-to-height tendency"
            };
        }

        private static string BucketNoseProjection(double prominence)
        {
            if (prominence < 0.25)
            {
                return "low projection";
            }

            if (prominence < 0.45)
            {
                return "moderate-low projection";
            }

            if (prominence < 0.65)
            {
                return "moderate projection";
            }

            if (prominence < 0.8)
            {
                return "moderate-high projection";
            }

            return "high projection";
        }

        private static string BucketLipFullness(double fullness)
        {
            if (fullness < 0.25)
            {
                return "thin";
            }

            if (fullness < 0.45)
            {
                return "medium-thin";
            }

            if (fullness < 0.65)
            {
                return "medium-full";
            }

            if (fullness < 0.8)
            {
                return "full";
            }

            return "very full";
        }
    }
}
