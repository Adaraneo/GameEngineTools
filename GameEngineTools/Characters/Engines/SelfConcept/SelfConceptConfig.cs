// SelfConceptConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SelfConcept
{
    /// <summary>
    /// Tuning parameters for the self-concept engine.
    /// All values bind from <c>Characters:SelfConcept</c> in appsettings.
    /// </summary>
    public sealed record SelfConceptConfig(
        /// <summary>
        /// Weight applied to feedback that <b>confirms</b> the current self-view
        /// (self-verification; Swann 1983). Default 0.6.
        /// </summary>
        double ConfirmingWeight = 0.6,

        /// <summary>
        /// Weight applied to feedback that <b>disconfirms</b> the current self-view —
        /// heavily discounted. Default 0.15.
        /// </summary>
        double DisconfirmingWeight = 0.15,

        /// <summary>
        /// Short-term affect amplifier for negative feedback. Applies only to transient affect,
        /// NOT to the long-term self-update. Kept for the affect pathway; default 1.7.
        /// </summary>
        double NegativeAffectMultiplier = 1.7,

        /// <summary>Base step size for perceived-trait updates per feedback event. Default 0.08.</summary>
        double PerceivedUpdateStep = 0.08,

        /// <summary>Base step size for self-esteem updates per feedback event (slower). Default 0.02.</summary>
        double EsteemUpdateStep = 0.02,

        /// <summary>
        /// Self-discrepancy above which a <c>BuildIdentity</c> goal is seeded. Default 0.35.
        /// </summary>
        double DiscrepancyThreshold = 0.35,

        /// <summary>Initial salience of the seeded BuildIdentity goal. Default 0.4.</summary>
        double BuildIdentitySeedSalience = 0.4,

        /// <summary>
        /// Minimum perceived-trait or esteem change before a <c>MetaperceptionUpdated</c> event
        /// is emitted. Default 0.01.
        /// </summary>
        double MetaperceptionEmitThreshold = 0.01
    )
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public SelfConceptConfig() : this(0.6, 0.15, 1.7, 0.08, 0.02, 0.35, 0.4, 0.01)
        { }
    }
}
