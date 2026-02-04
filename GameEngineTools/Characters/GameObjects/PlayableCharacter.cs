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
        public PC() : base()
        {
        }

        public PC(int maxHealth, IHuman person) : base(maxHealth, person)
        {
        }
    }
}
