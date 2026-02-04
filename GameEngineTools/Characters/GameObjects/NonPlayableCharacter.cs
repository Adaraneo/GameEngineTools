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
        public NPC() : base()
        {
        }

        public NPC(int maxHealth, IHuman person) : base(maxHealth, person)
        {
        }
    }
}
