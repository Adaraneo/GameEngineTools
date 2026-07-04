// JealousyType.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    /// <summary>
    /// Classifies a jealousy response by its trigger mechanism (Attridge 2013).
    /// </summary>
    /// <remarks>
    /// Source: Attridge, M. (2013). Jealousy and Relationship Closeness. <i>SAGE Open</i>, 3(1).
    /// Distinguishes emotional/reactive jealousy (adaptive, evidence-based, associated with
    /// relationship closeness/satisfaction) from cognitive/suspicious jealousy (maladaptive,
    /// evidence-independent, associated with anxious attachment and low trust).
    /// </remarks>
    public enum JealousyType
    {
        /// <summary>Triggered by an actually-observed threat event. GET's only implemented path.</summary>
        Reactive,

        /// <summary>
        /// Evidence-independent suspicion. NOT YET IMPLEMENTED — no peer-reviewed base rate or
        /// trigger frequency exists to cite; a suspicion-generation mechanism would require a new,
        /// separately-gated research pass (attachment-anxiety-driven periodic suspicion rate is not
        /// established in the literature reviewed for this topic).
        /// </summary>
        Suspicious
    }
}
