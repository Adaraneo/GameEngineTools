// PerceivedMeaning.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Contracts
{
    using System.Collections.Immutable;
    using Grammar.Core.Enums;

    /// <summary>
    /// A listener's subjective reading of a <see cref="SpeechAct"/>. Two listeners receive the same
    /// objective act but may produce divergent <see cref="PerceivedMeaning"/>s — the divergence is
    /// where subjectivity lives, not in parsing. The original act is always preserved in
    /// <see cref="Source"/> as an audit trail.
    /// </summary>
    public sealed record PerceivedMeaning
    {
        /// <summary>The unchanged act as it was sent (audit trail; the listener never mutates it).</summary>
        public required SpeechAct Source { get; init; }

        /// <summary>Illocutionary point as understood (may differ from surface when irony is decoded).</summary>
        public IllocutionaryPoint PerceivedPoint { get; init; }

        /// <summary>Polarity as understood — irony decoded vs taken literally.</summary>
        public Polarity PerceivedPolarity { get; init; }

        /// <summary>Directness as felt — a hostile listener shifts it toward Blunt.</summary>
        public Directness PerceivedDirectness { get; init; }

        /// <summary>Roles resolved against the listener's knowledge base (mis-resolution is possible).</summary>
        public required ImmutableDictionary<FgdFunctor, EntityRef> ResolvedRoles { get; init; }

        /// <summary>Deterministically derived confidence in this reading, [0..1].</summary>
        public double Confidence { get; init; }

        /// <summary>
        /// Additive connotation contribution from <see cref="SpeechAct.PredicateLemma"/>, [−1..1];
        /// 0 when the connotation layer is disabled. Independent of the grammatical
        /// <see cref="Polarity"/> — sentiment never leaks into sentence negation.
        /// </summary>
        public double ConnotationDelta { get; init; }

        /// <summary>
        /// Phase-2 power signal of the word choice (Sap 2017 connotation frames), [−1..1]: positive =
        /// the speaker's verb claims power over the addressee ("vyžadovat"), negative = it casts the
        /// speaker as subordinate ("žebrat"). 0 when the connotation layer is off. Consumed by
        /// <see cref="GameEngineTools.Characters.Engines.Relationships.DefaultRelationshipsEngine"/>
        /// (recipient-side Respect shift), gated behind
        /// <see cref="GameEngineTools.Characters.Engines.Relationships.RelationshipsConfig.EnablePowerRespectPropagation"/>
        /// (default <c>false</c>, so the baseline stays byte-identical until opted in).
        /// </summary>
        public double PerceivedPowerDelta { get; init; }

        /// <summary>
        /// Phase-2 agency signal of the word choice (Sap 2017), [−1..1]: positive = the verb portrays
        /// the speaker as high-agency / in control, negative = passive. 0 when the layer is off.
        /// Unlike <see cref="PerceivedPowerDelta"/>, still a prepared, not-yet-consumed signal as of
        /// 2026-08-29 — checked against the current call graph, no engine reads it yet.
        /// </summary>
        public double PerceivedAgencyDelta { get; init; }
    }
}
