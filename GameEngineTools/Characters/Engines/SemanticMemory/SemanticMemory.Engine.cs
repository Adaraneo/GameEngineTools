// SemanticMemory.Engine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Engine spravující sémantická přesvědčení postavy o ostatních lidech.
    /// Odvozuje beliefs z epizodické paměti a aktualizuje je při každém <see cref="GameEngineTools.Characters.Engines.Memory.MemoryEncoded"/> eventu.
    /// </summary>
    public interface ISemanticMemoryEngine : IEngine<SemanticMemoryState, SemanticMemoryConfig>
    {
        /// <summary>
        /// Vrátí všechna přesvědčení o dané osobě seřazená sestupně dle Strength.
        /// Prázdný seznam pokud postava o dané osobě nic neví.
        /// </summary>
        IReadOnlyList<PersonBelief> GetBeliefsSorted(HumanId other);

        /// <summary>
        /// Odstraní veškerá přesvědčení o dané osobě.
        /// No-op pokud postava o osobě žádné beliefs nemá.
        /// </summary>
        void ForgetPerson(HumanId other);
    }
}
