// PlayableCharacter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.GameObjects
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Playable character
    /// </summary>
    public class PC : CharacterBase
    {
        /// <summary>Creates an empty playable character (for deserialization).</summary>
        public PC() : base()
        {
        }

        /// <summary>Creates a playable character with the given max health and underlying person.</summary>
        /// <param name="maxHealth">Maximum health points.</param>
        /// <param name="person">The underlying simulated character.</param>
        public PC(int maxHealth, IHuman person) : base(maxHealth, person)
        {
        }
    }
}
