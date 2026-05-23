// SemanticMemory.State.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Immutable snapshot sémantické paměti — přesvědčení postavy o všech lidech, které zná.
    /// </summary>
    public sealed record SemanticMemoryState(
        IReadOnlyDictionary<HumanId, PersonBeliefSet> People)
    {
        /// <summary>Prázdný stav pro nové postavy bez jakýchkoli přesvědčení.</summary>
        public static SemanticMemoryState Empty { get; } =
            new(new Dictionary<HumanId, PersonBeliefSet>());

        /// <summary>
        /// Vrátí BeliefSet pro danou osobu, nebo <see langword="null"/> pokud postava osobu nezná.
        /// </summary>
        public PersonBeliefSet? GetBeliefs(HumanId other)
            => People.TryGetValue(other, out var beliefs) ? beliefs : null;

        /// <summary>
        /// Vrátí Strength daného belief kind pro osobu, nebo 0.0 pokud belief neexistuje.
        /// </summary>
        public double GetStrength(HumanId other, PersonBeliefKind kind)
            => GetBeliefs(other)?.StrengthOf(kind) ?? 0.0;

        /// <summary>
        /// Predikuje pravděpodobnost přijetí sociálního přístupu danou osobou.
        /// Zkrácený overload bez kontextu vztahu a psychologického profilu.
        /// </summary>
        public double ExpectedAcceptance(HumanId other, SpeechAct act)
            => SemanticMemoryMath.ExpectedAcceptance(this, other, act);

        /// <summary>
        /// Predikuje pravděpodobnost přijetí sociálního přístupu s plným kontextem.
        /// Zahrnuje vztahové metriky, psychologický profil a trend posledních epizod.
        /// </summary>
        public double ExpectedAcceptance(
            HumanId other,
            SpeechAct act,
            RelationshipEdge? relationship,
            PsychologicalProfile? profile,
            IReadOnlyList<EpisodicMemory>? episodes = null)
            => SemanticMemoryMath.ExpectedAcceptance(this, other, act, relationship, profile, episodes);
    }
}
