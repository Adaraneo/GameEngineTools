// SpeechAct.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Contracts
{
    using System.Collections.Immutable;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;

    /// <summary>
    /// A mismatch between the surface form of an act and its intended force — irony, rhetorical
    /// questions, and the like. <c>null</c> on a <see cref="SpeechAct"/> means the act is direct.
    /// </summary>
    /// <remarks>
    /// Irony is encoded as a <see cref="SurfacePolarity"/> opposite to the act's intended
    /// <see cref="SpeechAct.Polarity"/>. Whether the shift is decoded is the listener's job
    /// (a later appraisal phase), never a guaranteed property of the act itself.
    /// </remarks>
    /// <param name="SurfacePoint">The illocutionary point as it appears on the surface.</param>
    /// <param name="SurfacePolarity">The polarity as it appears on the surface.</param>
    public sealed record ForceShift(IllocutionaryPoint SurfacePoint, Polarity SurfacePolarity);

    /// <summary>
    /// The single, multi-dimensional unit of communication between characters. NPCs exchange
    /// <see cref="SpeechAct"/> records — never text; surface (Czech) realisation is a render for the
    /// player and never re-enters simulation state.
    /// </summary>
    /// <remarks>
    /// Roles are keyed by <see cref="FgdFunctor"/> from <c>Grammar.Core</c>; valency frames live on
    /// the GM side and pair with this act at realisation time via <see cref="PredicateLemma"/>. This
    /// is the single cross-repo contract with the Grammar (GM) project — any change here is escalated.
    /// </remarks>
    public sealed record SpeechAct
    {
        /// <summary>Searle's illocutionary point — the closed primary dimension.</summary>
        public required IllocutionaryPoint Point { get; init; }

        /// <summary>Relational/social flavour driving relationship-domain routing.</summary>
        public RelationalActKind RelationalKind { get; init; }

        /// <summary>Orthogonal ISO 24617-2 dimensions (feedback, turn, social obligation).</summary>
        public DialogueDimension Dimensions { get; init; }

        /// <summary>Predicate lemma carrying lexical content; pairs with a GM valency frame.</summary>
        public required string PredicateLemma { get; init; }

        /// <summary>Semantic roles (FGD functors → referents).</summary>
        public required ImmutableDictionary<FgdFunctor, EntityRef> Roles { get; init; }

        /// <summary>Intended polarity of the proposition.</summary>
        public Polarity Polarity { get; init; }

        /// <summary>Social register (drives tykání/vykání on the GM side).</summary>
        public Register Register { get; init; }

        /// <summary>Surface directness (Brown &amp; Levinson face work).</summary>
        public Directness Directness { get; init; }

        /// <summary>Surface-vs-intended force mismatch (irony); <c>null</c> for a direct act.</summary>
        public ForceShift? ForceShift { get; init; }

        /// <summary>The speaker of the act.</summary>
        public required EntityRef Speaker { get; init; }

        /// <summary>The addressee of the act.</summary>
        public required EntityRef Addressee { get; init; }

        /// <summary>When the act was produced (world time; never <c>System.DateTime</c>).</summary>
        public required WDateTime OccurredAt { get; init; }

        /// <summary>
        /// Builds a minimal, direct <see cref="SpeechAct"/> for the common relational case. The
        /// linguistic detail (a real <see cref="PredicateLemma"/>, richer roles, register/directness
        /// tuning) is filled by the <c>SpeechActPlanner</c> in a later phase; this factory keeps the
        /// interaction pipeline compiling and deterministic in the meantime.
        /// </summary>
        /// <param name="kind">Relational flavour of the act.</param>
        /// <param name="speaker">Speaker character id.</param>
        /// <param name="addressee">Addressee character id.</param>
        /// <param name="occurredAt">World time of the act.</param>
        /// <param name="speakerLemma">Optional nominal lemma captured for the speaker.</param>
        /// <param name="addresseeLemma">Optional nominal lemma captured for the addressee.</param>
        /// <returns>A well-formed direct <see cref="SpeechAct"/> carrying ACT and ADDR roles.</returns>
        public static SpeechAct Relational(
            RelationalActKind kind,
            HumanId speaker,
            HumanId addressee,
            WDateTime occurredAt,
            string speakerLemma = "",
            string addresseeLemma = "")
        {
            var speakerRef = EntityRef.ForHuman(speaker, speakerLemma);
            var addresseeRef = EntityRef.ForHuman(addressee, addresseeLemma);

            var roles = ImmutableDictionary<FgdFunctor, EntityRef>.Empty
                .Add(FgdFunctor.ACT, speakerRef)
                .Add(FgdFunctor.ADDR, addresseeRef);

            return new SpeechAct
            {
                Point = DefaultPointFor(kind),
                RelationalKind = kind,
                Dimensions = DefaultDimensionsFor(kind),
                PredicateLemma = string.Empty,
                Roles = roles,
                Polarity = Polarity.Affirmative,
                Register = Register.Informal,
                Directness = Directness.Neutral,
                ForceShift = null,
                Speaker = speakerRef,
                Addressee = addresseeRef,
                OccurredAt = occurredAt
            };
        }

        /// <summary>Maps a relational flavour to its default illocutionary point.</summary>
        public static IllocutionaryPoint DefaultPointFor(RelationalActKind kind) => kind switch
        {
            RelationalActKind.Question => IllocutionaryPoint.Question,
            RelationalActKind.SelfDisclosure => IllocutionaryPoint.Assertive,
            RelationalActKind.Meta => IllocutionaryPoint.Assertive,
            RelationalActKind.Boundary => IllocutionaryPoint.Directive,
            RelationalActKind.Invite => IllocutionaryPoint.Directive,
            _ => IllocutionaryPoint.Expressive
        };

        private static DialogueDimension DefaultDimensionsFor(RelationalActKind kind) => kind switch
        {
            RelationalActKind.SmallTalk => DialogueDimension.SocialObligation,
            _ => DialogueDimension.None
        };
    }
}
