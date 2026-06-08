// SemanticMemory.Engine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Engine that manages the character's semantic beliefs about other people.
    /// Derives beliefs from episodic memory and updates them on every <see cref="GameEngineTools.Characters.Engines.Memory.MemoryEncoded"/> event.
    /// </summary>
    public interface ISemanticMemoryEngine : IEngine<SemanticMemoryState, SemanticMemoryConfig>
    {
        /// <summary>
        /// Returns all beliefs about the given person sorted descending by Strength.
        /// An empty list if the character knows nothing about the person.
        /// </summary>
        IReadOnlyList<PersonBelief> GetBeliefsSorted(HumanId other);

        /// <summary>
        /// Removes all beliefs about the given person.
        /// A no-op if the character has no beliefs about the person.
        /// </summary>
        void ForgetPerson(HumanId other);
    }
}
