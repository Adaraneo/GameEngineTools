// PortraitPromptFormatter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using System.Text;

    /// <summary>
    /// Formats a strict human-readable portrait prompt from a deterministic portrait specification.
    /// </summary>
    public sealed class PortraitPromptFormatter : IPortraitPromptFormatter
    {
        /// <inheritdoc/>
        public string Format(PortraitSpec spec)
        {
            ArgumentNullException.ThrowIfNull(spec);

            var sb = new StringBuilder();
            sb.Append("Create a faithful, non-stylized portrait. ");
            sb.Append("Use the provided appearance data exactly. ");
            sb.Append("No beautification, no glamour styling, no fantasy reinterpretation. ");
            sb.Append($"Biology: {spec.Biology}. ");
            sb.Append($"Height: {spec.Body.HeightCm:0.##} cm ({spec.Body.HeightBucket}). ");
            sb.Append($"Frame: {spec.Body.Frame.ToString().ToLowerInvariant()}, {spec.Body.FrameImpression}, {spec.Body.PostureBucket}. ");
            sb.Append($"Shoulder breadth: {spec.Body.ShoulderBreadthCm:0.##} cm. ");
            sb.Append($"Hip breadth: {spec.Body.HipBreadthCm:0.##} cm. ");
            sb.Append($"Waist-to-hip ratio: {spec.Body.WaistToHipRatio:0.###}. ");
            sb.Append($"Skin: {spec.Skin.ToneLabel} skin, {spec.Skin.Lightness} lightness, {spec.Skin.Undertone} undertone, {spec.Skin.TexturePolicy}. ");
            sb.Append($"Eyes: {spec.Eyes.HueFamily} eyes, {spec.Eyes.IrisVariationRange} iris variation, {spec.Eyes.LimbalRingIntensity} limbal ring. ");
            sb.Append($"Hair: {spec.Hair.BaseColorFamily} hair, {spec.Hair.BrightnessRange} brightness, {spec.Hair.LengthBucket}, {spec.Hair.Straightness}, {spec.Hair.Texture}, {spec.Hair.VolumePolicy}. ");
            sb.Append($"Face: {spec.Face.ShapeLabel} face shape, {spec.Face.WidthHeightTendency}. ");
            sb.Append($"Eye scale: {spec.Face.EyeScaleBucket}. ");
            sb.Append($"Nose: {spec.Face.NoseProjectionBucket}. ");
            sb.Append($"Lips: {spec.Face.LipFullnessBucket}. ");
            sb.Append($"Jaw: {spec.Face.JawDefinitionBucket}. ");
            sb.Append($"Asymmetry: {spec.Face.FacialAsymmetryBucket}. ");
            sb.Append($"Expression: {spec.Expression.ExpressionLabel}, {spec.Expression.MouthState}, {spec.Expression.BrowTension}. ");

            if (spec.DistinctiveMarks.Count > 0)
            {
                sb.Append("Distinctive marks: ");
                sb.Append(string.Join(", ", spec.DistinctiveMarks));
                sb.Append(". ");
            }

            sb.Append("Preserve natural asymmetry. ");
            sb.Append("Do not smooth skin. ");
            sb.Append("Do not enlarge eyes. ");
            sb.Append("Do not enhance lips. ");
            sb.Append("Do not force a smile.");
            return sb.ToString();
        }
    }
}
