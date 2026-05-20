// PortraitPromptFormatter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using System.Text;

    /// <summary>
    /// Formats a natural-language portrait prompt from a deterministic portrait specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Designed for GPT Image generation models (gpt-image-1 / gpt-image-2).
    /// These models process prompts as natural language — they do not understand
    /// raw centimetre measurements or internal enum labels. This formatter therefore:
    /// <list type="bullet">
    ///   <item>Uses bucket labels from <see cref="BodyRenderSpec"/> instead of raw centimetre values.</item>
    ///   <item>Leads with a photo-style directive so the model enters "photorealism mode".</item>
    ///   <item>Places age and ancestry immediately after the style directive —
    ///         without age the model defaults to a generic ~25-year-old adult.</item>
    ///   <item>Emits bias-guard constraints at the end, where GPT Image models respect them best.</item>
    ///   <item>Emits bias-guard instructions conditionally, driven by <see cref="PortraitBiasGuard"/>
    ///         flags rather than hardcoded strings.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class PortraitPromptFormatter : IPortraitPromptFormatter
    {
        /// <inheritdoc/>
        public string Format(PortraitSpec spec)
        {
            ArgumentNullException.ThrowIfNull(spec);

            var sb = new StringBuilder();

            // ── 1. Photo-style directive ─────────────────────────────────────────
            // Must come first — sets the "mode" for the model.
            // GPT Image interprets the opening tokens as a style contract.
            sb.Append("Photorealistic portrait photograph. ");
            sb.Append("85mm lens, soft studio lighting, shallow depth of field. ");
            sb.Append("Honest and unposed. Natural skin texture throughout. ");

            // ── 2. Subject — age, ancestry, biology ──────────────────────────────
            // Age is the single most critical token for portrait fidelity.
            // Ancestry eliminates the Western-European default drift.
            sb.Append(spec.AgeLabel);
            if (spec.AncestryHint is not null)
                sb.Append($", {spec.AncestryHint}");
            sb.Append($", {spec.Biology}. ");

            // ── 3. Body — bucket labels only, never raw centimetres ───────────────
            // Image models do not understand "36.12 cm shoulder breadth".
            // They DO understand "broad shoulders", "petite frame", "tall".
            sb.Append($"{spec.Body.FrameImpression}, {spec.Body.HeightBucket}, ");
            sb.Append($"{spec.Body.ProportionBucket}, {spec.Body.PostureBucket}. ");

            // ── 4. Skin ───────────────────────────────────────────────────────────
            sb.Append($"{spec.Skin.ToneLabel} skin, {spec.Skin.Undertone} undertone, ");
            sb.Append($"{spec.Skin.TexturePolicy}. ");

            // ── 5. Face ───────────────────────────────────────────────────────────
            sb.Append($"{spec.Face.ShapeLabel} face shape, {spec.Face.WidthHeightTendency}. ");
            sb.Append($"Nose: {spec.Face.NoseProjectionBucket}. ");
            sb.Append($"Lips: {spec.Face.LipFullnessBucket}. ");
            sb.Append($"Eyes: {spec.Eyes.HueFamily}, {spec.Face.EyeScaleBucket}. ");
            sb.Append($"Jaw: {spec.Face.JawDefinitionBucket}. ");
            sb.Append($"Asymmetry: {spec.Face.FacialAsymmetryBucket}. ");

            // ── 6. Hair ───────────────────────────────────────────────────────────
            sb.Append($"{spec.Hair.BaseColorFamily} hair, {spec.Hair.LengthBucket}, ");
            sb.Append($"{spec.Hair.Straightness}, {spec.Hair.Texture}. ");

            // ── 7. Expression ─────────────────────────────────────────────────────
            // Derived from runtime psychology state — tired, tense, calm, etc.
            sb.Append($"Expression: {spec.Expression.ExpressionLabel}, ");
            sb.Append($"{spec.Expression.MouthState}, {spec.Expression.BrowTension}. ");

            // ── 8. Distinctive marks ──────────────────────────────────────────────
            if (spec.DistinctiveMarks.Count > 0)
            {
                sb.Append("Distinctive marks: ");
                sb.Append(string.Join(", ", spec.DistinctiveMarks));
                sb.Append(". ");
            }

            // ── 9. Bias guard — data-driven, negative instructions last ───────────
            // GPT Image models respect end-of-prompt negative constraints best.
            // Flags are read from PortraitBiasGuard — never hardcoded.
            if (spec.BiasGuard.ForbidSkinSmoothing)
                sb.Append("No skin smoothing. ");

            if (spec.BiasGuard.ForbidEyeEnlargement)
                sb.Append("No eye enlargement. ");

            if (spec.BiasGuard.ForbidLipEnhancement)
                sb.Append("No lip enhancement. ");

            if (spec.BiasGuard.ForbidSymmetryEnhancement)
                sb.Append("No symmetry correction. ");

            if (spec.BiasGuard.ForbidForcedSmile)
                sb.Append("No forced smile. ");

            if (spec.BiasGuard.ForbidAestheticReinterpretation)
                sb.Append("No beautification. No glamour reinterpretation. ");

            return sb.ToString();
        }
    }
}
