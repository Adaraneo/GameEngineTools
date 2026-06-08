// NonPlayableCharacter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.GameObjects
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Non-playable character
    /// </summary>
    public class NPC : CharacterBase
    {
        /// <summary>Creates an empty non-playable character (for deserialization).</summary>
        public NPC() : base()
        {
        }

        /// <summary>Creates a non-playable character with the given max health and underlying person.</summary>
        /// <param name="maxHealth">Maximum health points.</param>
        /// <param name="person">The underlying simulated character.</param>
        public NPC(int maxHealth, IHuman person) : base(maxHealth, person)
        {
        }
    }
}
