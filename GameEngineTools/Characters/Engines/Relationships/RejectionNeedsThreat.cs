// RejectionNeedsThreat.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Published by <see cref="DefaultRelationshipsEngine"/> when an intimate advance
    /// (<see cref="GameEngineTools.Characters.Engines.Interactions.SpeechAct.Invite"/>) is rejected.
    /// </summary>
    /// <remarks>
    /// Williams' Temporal Need-Threat Model (Hartgerink et al. 2015, k=120 Cyberball studies,
    /// mean d &gt; |1.4|): social exclusion simultaneously threatens four fundamental needs:
    /// <list type="number">
    ///   <item><b>Belonging</b> — immediate; maps to SocialNeed urgency.</item>
    ///   <item><b>Self-esteem</b> — sharp drop; maps to Valence / MoodBaseline.</item>
    ///   <item><b>Control</b> — loss of agency; maps to Dominance penalty.</item>
    ///   <item><b>Meaningful existence</b> — existential signal; maps to Stress spike.</item>
    /// </list>
    /// Handlers in PsychologyEngine and BehaviorEngine translate <see cref="Intensity"/>
    /// into proportional state changes across all four needs.
    /// </remarks>
    public sealed record RejectionNeedsThreat(
        WDateTime OccurredAt,

        /// <summary>The character who was rejected and whose needs are threatened.</summary>
        HumanId Rejected,

        /// <summary>
        /// Intensity multiplier [0.72 – 1.6] produced by
        /// <c>ComputeRejectionStingMultiplier</c> and modulated by attachment anxiety.
        /// 1.0 = baseline impact; &gt;1.0 = amplified (preoccupied attachment, low prior safety).
        /// </summary>
        double Intensity,

        /// <summary>
        /// <c>true</c> when the source was an <c>InviteIntimacy</c> speech act;
        /// <c>false</c> for SelfDisclosure or generic rejection.
        /// Intimate rejection receives the full four-need-threat; non-intimate a partial one.
        /// </summary>
        bool IsIntimateAdvance) : IDomainEvent;
}
