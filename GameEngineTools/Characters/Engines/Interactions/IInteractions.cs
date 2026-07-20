// IInteractions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Konfigurace <see cref="IInteractionEngine"/>.
    /// </summary>
    /// <param name="MisattributionRateBase">
    /// Base probability of misinterpreting an intent.
    /// Scales with the character's stress: the higher the stress, the more often others' intents are misread.
    /// Default: 0.15.
    /// </param>
    public sealed record InteractionConfig(
        double MisattributionRateBase = 0.15,
        /// <summary>
        /// How much ambient noise amplifies misattribution of social intent.
        /// At noise=1.0, the misattribution rate is multiplied by (1 + NoiseAttributionAmplifier).
        /// Default 0.4 → up to 40% extra misattribution at maximum noise.
        /// </summary>
        double NoiseAttributionAmplifier = 0.40)
    {
        /// <summary>Parameterless constructor required by the Options pattern.</summary>
        public InteractionConfig() : this(0.15, 0.40) { }
    }

    /// <summary>Event — the character entered a new context (location, privacy, noise, crowd).</summary>
    public sealed record ContextChanged(
        WDateTime OccurredAt,
        HumanId Human,
        string Location,
        bool HasPrivacy,
        double Noise,
        double Crowding,
        SurfaceKind Kind,

        /// <summary>
        /// Characters present at the new location who can witness interactions.
        /// Passed through to <see cref="InteractionSurface.Observers"/>.
        /// <c>null</c> = no known observers (default).
        /// </summary>
        System.Collections.Generic.IReadOnlyList<HumanId>? Observers = null,

        /// <summary>
        /// Active social norm context at the new location, or <c>null</c> for ordinary surfaces.
        /// Passed through to <see cref="InteractionSurface.NormContext"/>.
        /// </summary>
        SocialNormContext? NormContext = null) : IDomainEvent;

    /// <summary>
    /// Description of the current interaction environment — what is "at hand".
    /// Affects the probability of an interaction being accepted.
    /// </summary>
    public sealed record InteractionSurface(
        string? Location,
        bool HasPrivacy,
        double Noise,
        double Crowding,
        SurfaceKind Kind,
        /// <summary>
        /// Characters present at this location who can witness interactions.
        /// When non-null and non-empty, the RelationshipsEngine emits
        /// <see cref="GameEngineTools.Characters.Engines.Relationships.ThirdPartyActionObserved"/>
        /// events for each observer after processing MicroPositive / MicroNegative.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<HumanId>? Observers = null,

        /// <summary>
        /// Distance to the nearest person at this location, in metres.
        /// Used for Altman (1975) proxemics zone calculation.
        /// <c>null</c> = distance not measured / not relevant.
        /// </summary>
        double? ProxemicDistanceMeters = null,

        /// <summary>
        /// Optional social norm context active on this surface.
        /// When set, <see cref="GameEngineTools.Characters.Engines.Interactions.DefaultInteractionEngine"/>
        /// will apply an anticipatory shame appraisal before resolving interaction acceptance,
        /// and may emit <see cref="NormViolationOccurred"/> if the action proceeds despite a high score.
        /// <c>null</c> = no active norm constraint (default for most surfaces).
        /// </summary>
        SocialNormContext? NormContext = null);

    /// <summary>Functional kind of an interaction surface / location.</summary>
    public enum SurfaceKind
    {
        /// <summary>Unknown / unspecified.</summary>
        Unknown = 0,

        /// <summary>Social space.</summary>
        Social,

        /// <summary>Private space.</summary>
        Private,

        /// <summary>Work space.</summary>
        Work,

        /// <summary>Resting space.</summary>
        Rest,

        /// <summary>Public space.</summary>
        Public
    }

    /// <summary>Interface for the engine that drives social interactions.</summary>
    public interface IInteractionEngine : IEngine<InteractionSurface, InteractionConfig>
    { }

    /// <summary>Level of physical contact in a <see cref="TouchAttempted"/>.</summary>
    public enum TouchLevel
    {
        /// <summary>No touch.</summary>
        None,

        /// <summary>Light touch (shoulder, arm) — builds the Physical domain.</summary>
        Light,

        /// <summary>Friendly touch (a hug) — builds Physical and Comfort more strongly.</summary>
        Friendly,

        /// <summary>Intimate touch — requires high Closeness and Attraction.</summary>
        Intimate
    }

    /// <summary>
    /// Event — character A proposes an interaction to character B. The semantic payload is the
    /// structured <see cref="InteractionContent"/> (a <see cref="SpeechAct"/>); characters never
    /// exchange text.
    /// </summary>
    public sealed record InteractionProposed(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        InteractionContent Content,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null) : IDomainEvent
    {
        /// <summary>
        /// Builds an <see cref="InteractionProposed"/> from a relational act kind, wrapping a stub
        /// <see cref="SpeechAct"/> (via <see cref="SpeechAct.Relational"/>) in an
        /// <see cref="InteractionContent"/>. A convenience for producers and tests until the
        /// <c>SpeechActPlanner</c> becomes the sole author of richly-specified speech acts.
        /// </summary>
        public static InteractionProposed Of(
            WDateTime occurredAt,
            HumanId from,
            HumanId to,
            RelationalActKind kind,
            SexBiology? fromBiology = null,
            SexBiology? toBiology = null)
            => new(
                occurredAt,
                from,
                to,
                new InteractionContent(SpeechAct.Relational(kind, from, to, occurredAt)),
                fromBiology,
                toBiology);
    }

    /// <summary>Event — character A attempts physical contact with character B.</summary>
    public sealed record TouchAttempted(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        TouchLevel Level) : IDomainEvent;

    /// <summary>
    /// Event — the interaction was evaluated (accepted or declined).
    /// </summary>
    /// <param name="Act">
    /// The relational act kind from the original <see cref="InteractionProposed"/>.
    /// We carry it here so the <c>RelationshipsEngine</c> knows which domain to update
    /// without having to correlate with the original event.
    /// </param>
    public sealed record InteractionOutcome(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        bool Accepted,
        string Reason,
        RelationalActKind Act = RelationalActKind.SmallTalk,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null,
        double? PeakValence = null,
        double? EndValence = null) : IDomainEvent;

    /// <summary>Intent regarding a potential pregnancy in an abstract sexual encounter.</summary>
    public enum ReproductiveIntent
    {
        /// <summary>Actively avoiding pregnancy.</summary>
        AvoidPregnancy = 0,

        /// <summary>Indifferent to the reproductive outcome.</summary>
        Indifferent = 1,

        /// <summary>Open to pregnancy if it happens.</summary>
        OpenToPregnancy = 2,

        /// <summary>Actively trying to conceive.</summary>
        TryingForChild = 3
    }

    /// <summary>Coarse level of contraceptive protection for the reproductive calculation.</summary>
    public enum ContraceptionLevel
    {
        /// <summary>Not specified.</summary>
        Unspecified = 0,

        /// <summary>No contraception.</summary>
        None = 1,

        /// <summary>Low-effectiveness contraception.</summary>
        Low = 2,

        /// <summary>Moderate-effectiveness contraception.</summary>
        Moderate = 3,

        /// <summary>High-effectiveness contraception.</summary>
        High = 4
    }

    /// <summary>Event — an intimate encounter was proposed after an accepted relational initiative.</summary>
    public sealed record SexualEncounterProposed(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        ReproductiveIntent Intent = ReproductiveIntent.Indifferent,
        ContraceptionLevel Contraception = ContraceptionLevel.Unspecified,
        bool ReproductivePotential = false,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null) : IDomainEvent;

    /// <summary>Event — an abstract intimate encounter was accepted or declined.</summary>
    public sealed record SexualEncounterOutcome(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        bool Accepted,
        string Reason,
        ReproductiveIntent Intent = ReproductiveIntent.Indifferent,
        ContraceptionLevel Contraception = ContraceptionLevel.Unspecified,
        bool ReproductivePotential = false,
        SexBiology? FromBiology = null,
        SexBiology? ToBiology = null) : IDomainEvent;
}
