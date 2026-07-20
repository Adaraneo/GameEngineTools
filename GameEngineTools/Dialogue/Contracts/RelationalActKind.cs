// RelationalActKind.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Contracts
{
    /// <summary>
    /// Relational/social flavour of an interaction — determines which relationship domains an
    /// accepted act builds. This is a <i>dimension</i> of <see cref="SpeechAct"/> (see
    /// <see cref="SpeechAct.RelationalKind"/>), not a standalone communication type.
    /// </summary>
    /// <remarks>
    /// Formerly the standalone <c>SpeechAct</c> enum; folded into the multi-dimensional
    /// <see cref="SpeechAct"/> record so there is a single speech-act type across the engine and the
    /// GM surface. Member names, order, and values are preserved so relationship-domain routing is
    /// unchanged.
    /// </remarks>
    public enum RelationalActKind
    {
        /// <summary>Casual chat — builds Humor.</summary>
        SmallTalk,

        /// <summary>A question — shows interest, builds the Intellect domain.</summary>
        Question,

        /// <summary>Self-disclosure — sharing something personal — builds Values and Closeness.</summary>
        SelfDisclosure,

        /// <summary>Validation — affirming and supporting the other — builds Values and Comfort.</summary>
        Validation,

        /// <summary>Boundary — setting a limit in the interaction.</summary>
        Boundary,

        /// <summary>Humor — a joke, lightening the mood — strongly builds the Humor domain.</summary>
        Humor,

        /// <summary>Meta — commentary about the relationship or interaction itself — builds Intellect.</summary>
        Meta,

        /// <summary>Invite — social initiative — gently builds the Physical domain.</summary>
        Invite
    }
}
