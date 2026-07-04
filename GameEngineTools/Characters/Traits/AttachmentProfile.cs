// AttachmentProfile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>
    /// Two-dimensional continuous attachment model (Brennan, Clark &amp; Shaver 1998 ECR-R).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attachment is a <b>continuous latent space</b>, not four discrete types.
    /// Taxometric analyses (Fraley et al. 2015, <i>JPSP</i>) confirm no categorical
    /// boundary exists — use two floats, never an enum.
    /// </para>
    /// <para>
    /// The dimensions modulate how every <see cref="GameEngineTools.Characters.Engines.Relationships.RelationshipEdge"/>
    /// dimension updates:
    /// <list type="bullet">
    ///   <item><b>High Anxiety</b> — hypervigilant rejection detection, reassurance-seeking,
    ///     dramatic trust spikes on perceived abandonment (hyperactivation strategy).</item>
    ///   <item><b>High Avoidance</b> — suppressed self-disclosure, lower Closeness ceiling,
    ///     withdrawal under conflict, repair attempts treated as intrusion
    ///     (deactivation strategy).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Relationship to transference (<see cref="GameEngineTools.Characters.Engines.SemanticMemory.SignificantOtherImprint"/>):</b>
    /// AttachmentProfile is a general, trait-like relational style applied to every new person.
    /// Transference is a separate, resemblance-gated, memory-activated perturbation that only
    /// applies to specific new people who resemble a specific past significant other, and decays
    /// quickly as real evidence accrues. The two are complementary and must never be merged into a
    /// single computation (Brumbaugh &amp; Fraley 2006 empirically demonstrate both operating
    /// simultaneously: general attachment style applies to all targets, more strongly when the
    /// target resembles a past partner).
    /// </para>
    /// </remarks>
    public sealed record AttachmentProfile(
        /// <summary>
        /// Attachment anxiety dimension [0–1].
        /// 0 = no rejection sensitivity; 1 = hypervigilant, dramatic response to perceived abandonment.
        /// </summary>
        double Anxiety,

        /// <summary>
        /// Attachment avoidance dimension [0–1].
        /// 0 = open; 1 = suppressed self-disclosure, hard Closeness ceiling, repair is intrusive.
        /// </summary>
        double Avoidance)
    {
        /// <summary>
        /// Low anxiety, low avoidance — proportional, constructive, fast Trust recovery.
        /// Baseline (0, 0): no amplification of any dimension.
        /// </summary>
        public static AttachmentProfile Secure => new(0.0, 0.0);

        /// <summary>High anxiety, low avoidance — reassurance-seeking, jealousy, slow Trust decay with dramatic spikes.</summary>
        public static AttachmentProfile Preoccupied => new(0.8, 0.15);

        /// <summary>Low anxiety, high avoidance — suppressed intimacy, withdrawal under conflict.</summary>
        public static AttachmentProfile Dismissing => new(0.15, 0.8);

        /// <summary>High anxiety, high avoidance — unstable; alternates approach and withdrawal.</summary>
        public static AttachmentProfile Fearful => new(0.8, 0.8);
    }
}
